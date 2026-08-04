"""Face detection + recognition using OpenCV's DNN stack.

- Detection:  YuNet  (cv2.FaceDetectorYN)
- Recognition: SFace (cv2.FaceRecognizerSF) -> 128-d embedding per face

Each enrolled student is stored as a set of L2-normalized embeddings in
`data/embeddings/<id>.npy`. Recognition compares a live embedding against all
stored embeddings via cosine similarity (higher = more similar).
"""
from __future__ import annotations

import os
import threading
import time
from typing import Optional, Union

import cv2
import numpy as np

import config

CameraSource = Union[int, str]


def resolve_camera_source(
    source: Optional[CameraSource] = None,
) -> CameraSource:
    """Prefer explicit source, then CAMERA_SOURCE URL, else CAMERA_INDEX."""
    if source is not None:
        return source
    if config.CAMERA_SOURCE:
        return config.CAMERA_SOURCE
    return config.CAMERA_INDEX


def find_working_camera_index(max_index: int = 4) -> int:
    """Pick the first USB webcam index that returns a real frame."""
    for index in range(max_index):
        cap = None
        try:
            backends = [cv2.CAP_DSHOW, cv2.CAP_MSMF, 0] if os.name == "nt" else [0]
            for backend in backends:
                cap = cv2.VideoCapture(index, backend) if backend else cv2.VideoCapture(index)
                if not cap.isOpened():
                    cap.release()
                    cap = None
                    continue
                ok, frame = cap.read()
                if ok and frame is not None and frame.size > 0:
                    return index
                cap.release()
                cap = None
        finally:
            if cap is not None:
                cap.release()
    return config.CAMERA_INDEX


class Camera:
    """Thread-safe wrapper around a single cv2.VideoCapture.

    Supports USB webcam index (default) or a live RTSP/HTTP URL via
    CAMERA_SOURCE — same open path works for campus IP cameras later.
    """

    def __init__(self, source: Optional[CameraSource] = None):
        self.source = resolve_camera_source(source)
        self._cap: Optional[cv2.VideoCapture] = None
        self._lock = threading.Lock()
        self._fail_count = 0
        self._last_reopen = 0.0

    def _open_capture(self) -> cv2.VideoCapture:
        source = self.source
        # Network / file URL (RTSP, HTTP MJPEG, etc.)
        if isinstance(source, str) and not source.isdigit():
            cap = cv2.VideoCapture(source)
        else:
            index = int(source)
            cap = None
            backends = [cv2.CAP_DSHOW, cv2.CAP_MSMF, 0] if os.name == "nt" else [0]
            for backend in backends:
                trial = cv2.VideoCapture(index, backend) if backend else cv2.VideoCapture(index)
                if trial.isOpened():
                    cap = trial
                    break
                trial.release()
            if cap is None:
                cap = cv2.VideoCapture(index)

        if cap.isOpened():
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, config.FRAME_WIDTH)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, config.FRAME_HEIGHT)
            # Warm up — first frames are often blank on Windows webcams.
            for _ in range(8):
                cap.read()
        return cap

    def _release_locked(self) -> None:
        if self._cap is not None:
            self._cap.release()
            self._cap = None

    def _ensure_open(self) -> cv2.VideoCapture:
        if self._cap is None or not self._cap.isOpened():
            self._release_locked()
            self._cap = self._open_capture()
        return self._cap

    def read(self):
        with self._lock:
            cap = self._ensure_open()
            ok, frame = cap.read()
            if ok and frame is not None and frame.size > 0:
                self._fail_count = 0
                return frame

            self._fail_count += 1
            now = time.time()
            if self._fail_count >= 3 and now - self._last_reopen > 2.0:
                self._last_reopen = now
                self._fail_count = 0
                self._release_locked()
                cap = self._ensure_open()
                ok, frame = cap.read()
                if ok and frame is not None and frame.size > 0:
                    return frame
        return None

    def is_available(self) -> bool:
        with self._lock:
            cap = self._ensure_open()
            if not cap.isOpened():
                return False
            ok, frame = cap.read()
            return ok and frame is not None and frame.size > 0

    def release(self) -> None:
        with self._lock:
            self._release_locked()


class FaceEngine:
    def __init__(self):
        for path in (config.YUNET_PATH, config.SFACE_PATH):
            if not path.exists():
                raise RuntimeError(
                    f"Missing model file: {path}. Run the download step (see README)."
                )

        self._infer_lock = threading.Lock()
        self.detector = cv2.FaceDetectorYN.create(
            str(config.YUNET_PATH),
            "",
            (config.FRAME_WIDTH, config.FRAME_HEIGHT),
            config.YUNET_SCORE_THRESHOLD,
            config.YUNET_NMS_THRESHOLD,
            config.YUNET_TOP_K,
        )
        self.recognizer = cv2.FaceRecognizerSF.create(str(config.SFACE_PATH), "")

        # Stacked embedding index for fast cosine matching.
        self._matrix: Optional[np.ndarray] = None  # (M, 128) normalized
        self._labels: np.ndarray = np.empty(0, dtype=int)  # (M,) student ids

        config.ensure_dirs()
        self.load_embeddings()

    @property
    def trained(self) -> bool:
        return self._matrix is not None and len(self._labels) > 0

    # ------------------------------------------------------------------ #
    # Detection + embedding (DNN calls serialized via _infer_lock)
    # ------------------------------------------------------------------ #
    def _detect(self, frame) -> np.ndarray:
        h, w = frame.shape[:2]
        with self._infer_lock:
            self.detector.setInputSize((w, h))
            _, faces = self.detector.detect(frame)
        return faces if faces is not None else np.empty((0, 15), dtype=np.float32)

    def _embed(self, frame, face_row) -> np.ndarray:
        with self._infer_lock:
            aligned = self.recognizer.alignCrop(frame, face_row)
            feat = self.recognizer.feature(aligned)
        vec = np.asarray(feat, dtype=np.float32).flatten()
        norm = np.linalg.norm(vec)
        return vec / norm if norm > 0 else vec

    @staticmethod
    def _largest(faces: np.ndarray):
        if len(faces) == 0:
            return None
        return faces[int(np.argmax(faces[:, 2] * faces[:, 3]))]

    # ------------------------------------------------------------------ #
    # Embedding store / index
    # ------------------------------------------------------------------ #
    def load_embeddings(self) -> None:
        vectors: list[np.ndarray] = []
        labels: list[int] = []
        for npy in sorted(config.EMBEDDINGS_DIR.glob("*.npy")):
            if not npy.stem.isdigit():
                continue
            student_id = int(npy.stem)
            arr = np.load(npy)
            if arr.ndim == 1:
                arr = arr[None, :]
            for row in arr:
                vectors.append(row.astype(np.float32))
                labels.append(student_id)

        with self._infer_lock:
            if vectors:
                self._matrix = np.vstack(vectors)
                self._labels = np.array(labels, dtype=int)
            else:
                self._matrix = None
                self._labels = np.empty(0, dtype=int)

    def reload(self) -> int:
        self.load_embeddings()
        return int(len(self._labels))

    def delete_student_data(self, student_id: int) -> None:
        npy = config.EMBEDDINGS_DIR / f"{student_id}.npy"
        if npy.exists():
            npy.unlink()
        # Remove legacy face preview crops if present (no longer written).
        preview = config.FACES_DIR / f"{student_id}.png"
        if preview.exists():
            preview.unlink()
        self.load_embeddings()

    # ------------------------------------------------------------------ #
    # Enrollment
    # ------------------------------------------------------------------ #
    def embeddings_from_image(
        self, frame, repeats: int = 5
    ) -> tuple[Optional[np.ndarray], Optional[np.ndarray]]:
        """Build embeddings from a still photo (no camera)."""
        face = self._largest(self._detect(frame))
        if face is None:
            return None, None
        embeddings: list[np.ndarray] = []
        preview_crop: Optional[np.ndarray] = None
        for _ in range(max(1, repeats)):
            embeddings.append(self._embed(frame, face))
            if preview_crop is None:
                with self._infer_lock:
                    preview_crop = self.recognizer.alignCrop(frame, face)
        return np.vstack(embeddings), preview_crop

    def capture_samples(
        self, camera: Camera, student_id: int, count: int = config.ENROLL_SAMPLES
    ) -> int:
        """Capture and immediately persist samples for an existing student."""
        embeddings, crop = self.capture_new(camera, count)
        if embeddings is None:
            return 0
        self.save_student_embeddings(student_id, embeddings, crop)
        return int(embeddings.shape[0])

    def capture_new(
        self, camera: Camera, count: int = config.ENROLL_SAMPLES
    ) -> tuple[Optional[np.ndarray], Optional[np.ndarray]]:
        """Capture face samples WITHOUT persisting them.

        Returns (embeddings (N,128) or None, preview_crop BGR image or None).
        Lets the caller run the face-verification step and collect details
        before an identity is committed to disk.
        """
        embeddings: list[np.ndarray] = []
        preview_crop: Optional[np.ndarray] = None
        attempts = 0
        max_attempts = count * 60

        while len(embeddings) < count and attempts < max_attempts:
            attempts += 1
            frame = camera.read()
            if frame is None:
                time.sleep(0.05)
                continue
            face = self._largest(self._detect(frame))
            if face is None:
                continue
            embeddings.append(self._embed(frame, face))
            if preview_crop is None:
                with self._infer_lock:
                    preview_crop = self.recognizer.alignCrop(frame, face)
            time.sleep(0.08)  # brief pause encourages pose variety

        if not embeddings:
            return None, None
        return np.vstack(embeddings), preview_crop

    def save_student_embeddings(
        self,
        student_id: int,
        embeddings: np.ndarray,
        preview_crop: Optional[np.ndarray] = None,
    ) -> int:
        """Persist face embeddings only (no face image files on disk).

        preview_crop is kept for API compatibility with enrollment callers;
        it is used only for in-session UI and is not written to disk.
        """
        del preview_crop  # intentionally not persisted
        np.save(config.EMBEDDINGS_DIR / f"{student_id}.npy", embeddings)
        self.load_embeddings()
        return int(embeddings.shape[0])

    def verify_against_index(
        self, embeddings: np.ndarray
    ) -> tuple[Optional[int], float]:
        """Face-verification helper: is this captured face already enrolled?

        Averages the captured embeddings and matches against the existing
        index. Returns (student_id, score) if a confident match is found,
        else (None, best_score).
        """
        if embeddings is None or len(embeddings) == 0:
            return None, 0.0
        mean = embeddings.mean(axis=0)
        norm = np.linalg.norm(mean)
        if norm > 0:
            mean = mean / norm
        return self._match(mean.astype(np.float32))

    # ------------------------------------------------------------------ #
    # Recognition
    # ------------------------------------------------------------------ #
    def _match(self, embedding: np.ndarray) -> tuple[Optional[int], float]:
        with self._infer_lock:
            if self._matrix is None:
                return None, 0.0
            sims = self._matrix @ embedding  # cosine (both L2-normalized)
            idx = int(np.argmax(sims))
            best = float(sims[idx])
            student_id = int(self._labels[idx])
        if best >= config.SFACE_COSINE_THRESHOLD:
            return student_id, best
        return None, best

    def recognize(self, frame) -> list[dict]:
        """Detect faces and predict identities.

        Returns list of {box:(x,y,w,h), student_id (or None), score}.
        """
        results: list[dict] = []
        for face in self._detect(frame):
            x, y, w, h = (int(v) for v in face[:4])
            student_id, score = (None, 0.0)
            if self.trained:
                student_id, score = self._match(self._embed(frame, face))
            results.append(
                {"box": (x, y, w, h), "student_id": student_id, "score": score}
            )
        return results
