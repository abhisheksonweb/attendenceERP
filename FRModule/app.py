"""Flask web app: recognition-only face attendance module.

Classes and students are synced from the ASP.NET Medical College portal via
API. This module provides live face recognition, attendance tracking, CSV
export, and face-enrollment APIs for the portal.
"""
from __future__ import annotations

import base64
import csv
import functools
import hashlib
import hmac
import io
import json
import threading
import time
import uuid
import urllib.error
import urllib.request
from datetime import date, datetime

import cv2
import numpy as np
from flask import (
    Flask,
    Response,
    abort,
    flash,
    jsonify,
    redirect,
    render_template,
    request,
    session,
    url_for,
)
from flask_cors import CORS
from werkzeug.security import check_password_hash

import ai_assistant
import config
import database
from attendance_logic import AttendanceManager
from face_engine import Camera, FaceEngine, find_working_camera_index

app = Flask(__name__)
app.secret_key = "face-attendance-demo-secret"

CORS(
    app,
    resources={
        r"/api/*": {"origins": config.ASPNET_CORS_ORIGINS},
        r"/classes/*/capture": {
            "origins": config.ASPNET_CORS_ORIGINS,
            "methods": ["POST", "OPTIONS"],
        },
        r"/classes/*/students": {
            "origins": config.ASPNET_CORS_ORIGINS,
            "methods": ["POST", "OPTIONS"],
        },
    },
    supports_credentials=False,
    allow_headers=["Content-Type", "X-Api-Key"],
)

database.init_db()
if config.CAMERA_SOURCE:
    camera = Camera()
    print(f"[FRM] Camera source: {config.CAMERA_SOURCE}")
else:
    cam_index = config.CAMERA_INDEX
    probe = Camera(cam_index)
    if not probe.is_available():
        detected = find_working_camera_index()
        if detected != cam_index:
            print(f"[FRM] Camera index {cam_index} unavailable; using index {detected}")
            cam_index = detected
        probe.release()
    else:
        probe.release()
    camera = Camera(cam_index)
    print(f"[FRM] USB camera ready on index {cam_index}")
engine = FaceEngine()
manager = AttendanceManager()

GREEN = (0, 180, 0)
RED = (0, 0, 220)
AMBER = (0, 165, 255)
GREY = (140, 140, 140)

# Pending face captures awaiting enrollment: token -> capture state.
_pending: dict[str, dict] = {}
_pending_lock = threading.Lock()
PENDING_TTL_SECONDS = 300

# While True, skip IN/OUT marking (used during face enrollment / capture).
_enrollment_pause = threading.Event()
# Classes currently watched via admin Face Recognition video feed.
_live_viewers: dict[int, float] = {}
_live_lock = threading.Lock()
LIVE_VIEWER_TTL_SECONDS = 15


def _mark_live_viewer(class_id: int) -> None:
    with _live_lock:
        _live_viewers[class_id] = time.time()


def _is_live_viewer(class_id: int) -> bool:
    with _live_lock:
        last = _live_viewers.get(class_id, 0.0)
        return (time.time() - last) < LIVE_VIEWER_TTL_SECONDS


def _can_mark_attendance(class_id: int) -> bool:
    """Attendance only while admin Face Recognition is open (or optional headless)."""
    if _enrollment_pause.is_set():
        return False
    if config.HEADLESS_RECOGNIZE:
        return True
    return _is_live_viewer(class_id)

# --------------------------------------------------------------------------- #
# Auth helpers
# --------------------------------------------------------------------------- #
def current_admin():
    admin_id = session.get("admin_id")
    if admin_id is None:
        return None
    return database.get_admin(admin_id)


def _api_key_from_request() -> str:
    return (
        request.headers.get("X-Api-Key")
        or request.args.get("api_key")
        or ""
    ).strip()


def _valid_api_key() -> bool:
    key = _api_key_from_request()
    return bool(key) and hmac.compare_digest(key, config.API_KEY)


def make_recognition_token(class_id: int) -> str:
    msg = str(class_id).encode()
    key = config.API_KEY.encode()
    return hmac.new(key, msg, hashlib.sha256).hexdigest()


def validate_recognition_token(class_id: int, token: str) -> bool:
    if not token:
        return False
    expected = make_recognition_token(class_id)
    return hmac.compare_digest(expected, token)


def login_required(view):
    @functools.wraps(view)
    def wrapped(*args, **kwargs):
        if session.get("admin_id") is None:
            flash("Please log in to continue.", "error")
            return redirect(url_for("login"))
        return view(*args, **kwargs)

    return wrapped


def api_key_required(view):
    @functools.wraps(view)
    def wrapped(*args, **kwargs):
        if not _valid_api_key():
            return jsonify({"ok": False, "error": "Unauthorized"}), 401
        return view(*args, **kwargs)

    return wrapped


def api_key_or_login_required(view):
    @functools.wraps(view)
    def wrapped(*args, **kwargs):
        if session.get("admin_id") is not None or _valid_api_key():
            return view(*args, **kwargs)
        if request.path.startswith("/api/") or request.is_json:
            return jsonify({"ok": False, "error": "Unauthorized"}), 401
        flash("Please log in to continue.", "error")
        return redirect(url_for("login"))

    return wrapped


def login_or_token_required(view):
    """Allow logged-in admin, API key, or a valid recognition token."""

    @functools.wraps(view)
    def wrapped(*args, **kwargs):
        class_id = kwargs.get("class_id")
        token = request.args.get("token", "")
        if session.get("admin_id") is not None or _valid_api_key():
            return view(*args, **kwargs)
        if class_id is not None and validate_recognition_token(class_id, token):
            return view(*args, **kwargs)
        if request.path.endswith("/api/attendance") or request.is_json:
            return jsonify({"ok": False, "error": "Unauthorized"}), 401
        abort(403)

    return wrapped


@app.context_processor
def inject_admin():
    admin = current_admin()
    return {"current_admin": admin}


def _class_or_404(class_id: int):
    cls = database.get_class(class_id)
    if cls is None:
        abort(404)
    return cls


def _owned_class_or_404(class_id: int):
    cls = database.get_class(class_id)
    if cls is None or cls["admin_id"] != session.get("admin_id"):
        abort(404)
    return cls


def _can_access_class(class_id: int) -> bool:
    if session.get("admin_id") is not None:
        cls = database.get_class(class_id)
        return cls is not None and cls["admin_id"] == session.get("admin_id")
    token = request.args.get("token", "")
    return validate_recognition_token(class_id, token)


# --------------------------------------------------------------------------- #
# Pending capture store
# --------------------------------------------------------------------------- #
def _prune_pending() -> None:
    now = time.time()
    with _pending_lock:
        stale = [t for t, v in _pending.items() if now - v["ts"] > PENDING_TTL_SECONDS]
        for t in stale:
            _pending.pop(t, None)


# --------------------------------------------------------------------------- #
# Video streaming
# --------------------------------------------------------------------------- #
def _placeholder(text: str) -> bytes:
    img = np.zeros((config.FRAME_HEIGHT, config.FRAME_WIDTH, 3), dtype=np.uint8)
    cv2.putText(
        img, text, (30, config.FRAME_HEIGHT // 2),
        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2, cv2.LINE_AA,
    )
    ok, buf = cv2.imencode(".jpg", img)
    return buf.tobytes()


# Cooldown map: (camera_class_id, student_id) -> last alert unix time
_wrong_class_alerts: dict[tuple[int, int], float] = {}
_wrong_class_lock = threading.Lock()


def _notify_portal_wrong_class(camera_class_id: int, student_id: int) -> None:
    """Tell ASP.NET when a student from another class appears on this camera."""
    now = time.time()
    key = (camera_class_id, student_id)
    with _wrong_class_lock:
        last = _wrong_class_alerts.get(key, 0.0)
        if now - last < config.WRONG_CLASS_COOLDOWN_SECONDS:
            return
        _wrong_class_alerts[key] = now

    student = database.get_student(student_id)
    cam_cls = database.get_class(camera_class_id)
    home_cls = None
    if student is not None:
        home_cls = database.get_class(int(student["class_id"]))

    payload = {
        "cameraFrmClassId": camera_class_id,
        "cameraClassExternalId": (cam_cls["external_id"] if cam_cls is not None else "") or "",
        "studentFrmId": student_id,
        "studentExternalId": (student["external_id"] if student is not None else "") or "",
        "studentName": (student["name"] if student is not None else "") or "",
        "rollNo": (student["roll_no"] if student is not None else "") or "",
        "homeClassName": (home_cls["name"] if home_cls is not None else "") or "",
    }

    url = f"{config.PORTAL_BASE_URL}/api/frm/wrong-class"
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=data,
        method="POST",
        headers={
            "Content-Type": "application/json",
            "X-Api-Key": config.API_KEY,
        },
    )

    def _send() -> None:
        try:
            with urllib.request.urlopen(req, timeout=3) as resp:
                resp.read()
        except (urllib.error.URLError, TimeoutError, OSError):
            pass

    threading.Thread(target=_send, daemon=True).start()


def _normalize_mode(mode: str | None) -> str:
    """Normalize attendance mode to 'in', 'out', or 'toggle'."""
    value = (mode or "in").strip().lower()
    if value in ("in", "checkin", "check-in", "entry"):
        return "in"
    if value in ("out", "checkout", "check-out", "exit"):
        return "out"
    if value in ("toggle", "auto"):
        return "toggle"
    return "in"


def _annotate(
    frame,
    class_id: int,
    allowed: set[int],
    names: dict[int, str],
    mode: str = "in",
) -> None:
    for det in engine.recognize(frame):
        x, y, w, h = det["box"]
        student_id = det["student_id"]
        if student_id is None:
            color, label = RED, "Unknown"
        elif student_id in allowed:
            if _can_mark_attendance(class_id):
                outcome = manager.process(student_id, class_id, mode=mode)
                status = outcome["status"]
                color = GREEN if status == "IN" else AMBER
                label = f"{names.get(student_id, f'ID {student_id}')} [{status}]"
            else:
                color, label = GREY, f"{names.get(student_id, f'ID {student_id}')} (preview)"
        else:
            color, label = GREY, "Other class"
            if _can_mark_attendance(class_id):
                _notify_portal_wrong_class(class_id, student_id)
        cv2.rectangle(frame, (x, y), (x + w, y + h), color, 2)
        cv2.rectangle(frame, (x, y - 22), (x + w, y), color, -1)
        cv2.putText(
            frame, label, (x + 4, y - 6),
            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1, cv2.LINE_AA,
        )


def _frames(class_id: int, mode: str = "in"):
    mode = _normalize_mode(mode)
    allowed = database.class_student_ids(class_id)
    names = database.student_name_map(class_id)
    frame_count = 0
    while True:
        _mark_live_viewer(class_id)
        frame = camera.read()
        if frame is None:
            payload = _placeholder("Camera unavailable")
            yield (b"--frame\r\nContent-Type: image/jpeg\r\n\r\n" + payload + b"\r\n")
            time.sleep(0.5)
            continue

        frame_count += 1
        if frame_count % 30 == 0:
            allowed = database.class_student_ids(class_id)
            names = database.student_name_map(class_id)

        _annotate(frame, class_id, allowed, names, mode=mode)
        ok, buf = cv2.imencode(".jpg", frame)
        if ok:
            yield (
                b"--frame\r\nContent-Type: image/jpeg\r\n\r\n"
                + buf.tobytes()
                + b"\r\n"
            )
        time.sleep(0.03)


@app.route("/classes/<int:class_id>/video_feed")
def class_video_feed(class_id: int):
    if not _can_access_class(class_id):
        abort(403)
    _class_or_404(class_id)
    mode = _normalize_mode(request.args.get("mode"))
    return Response(
        _frames(class_id, mode=mode),
        mimetype="multipart/x-mixed-replace; boundary=frame",
    )


def _raw_frames():
    """Plain preview stream (no recognition) for enrollment UI."""
    while True:
        frame = camera.read()
        if frame is None:
            payload = _placeholder("Camera unavailable")
            yield (b"--frame\r\nContent-Type: image/jpeg\r\n\r\n" + payload + b"\r\n")
            time.sleep(0.5)
            continue
        ok, buf = cv2.imencode(".jpg", frame)
        if ok:
            yield (
                b"--frame\r\nContent-Type: image/jpeg\r\n\r\n"
                + buf.tobytes()
                + b"\r\n"
            )
        time.sleep(0.03)


@app.route("/preview_feed")
def preview_feed():
    """Live camera preview for face enrollment. Requires API key or login."""
    if session.get("admin_id") is None and not _valid_api_key():
        abort(403)
    return Response(
        _raw_frames(), mimetype="multipart/x-mixed-replace; boundary=frame"
    )


# --------------------------------------------------------------------------- #
# Auth pages
# --------------------------------------------------------------------------- #
@app.route("/register", methods=["GET", "POST"])
def register():
    flash("Admin registration is disabled. Classes are managed in the Medical College portal.", "error")
    return redirect(url_for("login"))


@app.route("/login", methods=["GET", "POST"])
def login():
    if session.get("admin_id"):
        return redirect(url_for("classes"))
    if request.method == "POST":
        username = (request.form.get("username") or "").strip()
        password = request.form.get("password") or ""
        admin = database.get_admin_by_username(username)
        if admin is None or not check_password_hash(admin["password_hash"], password):
            flash("Invalid username or password.", "error")
            return redirect(url_for("login"))
        session["admin_id"] = admin["id"]
        flash(f"Welcome back, {username}.", "success")
        return redirect(url_for("classes"))
    return render_template("login.html")


@app.route("/logout", methods=["POST"])
def logout():
    session.clear()
    flash("Logged out.", "success")
    return redirect(url_for("login"))


# --------------------------------------------------------------------------- #
# Classes (read-only list for logged-in users)
# --------------------------------------------------------------------------- #
@app.route("/")
def classes():
    if session.get("admin_id") is None:
        flash("Classes are managed in the Medical College portal.", "error")
        return redirect(url_for("login"))
    admin_id = session["admin_id"]
    class_rows = database.list_classes(admin_id)
    for c in class_rows:
        c["recognize_token"] = make_recognition_token(c["id"])
    return render_template("classes.html", classes=class_rows)


@app.route("/classes/<int:class_id>")
@login_required
def class_detail(class_id: int):
    cls = _owned_class_or_404(class_id)
    return render_template(
        "class_detail.html",
        cls=cls,
        rows=database.dashboard_rows(class_id),
        students=database.list_students(class_id),
        student_count=len(database.list_students(class_id)),
        trained=engine.trained,
        today=date.today().isoformat(),
        ai_examples=ai_assistant.EXAMPLE_QUESTIONS,
        ai_enabled=config.ai_enabled(),
        recognize_token=make_recognition_token(class_id),
    )


@app.route("/recognize/<int:class_id>")
def recognize(class_id: int):
    token = request.args.get("token", "")
    if not validate_recognition_token(class_id, token):
        abort(403)
    cls = _class_or_404(class_id)
    return render_template(
        "recognize.html",
        cls=cls,
        rows=database.dashboard_rows(class_id),
        student_count=len(database.list_students(class_id)),
        trained=engine.trained,
        token=token,
    )


# --------------------------------------------------------------------------- #
# Face enrollment APIs (called by ASP.NET portal)
# --------------------------------------------------------------------------- #
@app.route("/classes/<int:class_id>/capture", methods=["POST"])
@api_key_or_login_required
def capture_face(class_id: int):
    if session.get("admin_id") is not None:
        _owned_class_or_404(class_id)
    else:
        _class_or_404(class_id)
    _prune_pending()

    if not camera.is_available():
        return jsonify({"ok": False, "error": "Camera unavailable."}), 400

    # Pause attendance marking while enrolling a face (preview/capture only).
    _enrollment_pause.set()
    try:
        embeddings, crop = engine.capture_new(camera, config.ENROLL_SAMPLES)
    finally:
        _enrollment_pause.clear()

    if embeddings is None:
        return jsonify(
            {"ok": False, "error": "No face detected. Look at the camera and retry."}
        ), 200

    match_id, score = engine.verify_against_index(embeddings)
    duplicate = None
    if match_id is not None:
        matched = database.get_student(match_id)
        if matched is not None:
            duplicate = {
                "name": matched["name"],
                "roll_no": matched["roll_no"],
                "same_class": matched["class_id"] == class_id,
                "score": round(float(score), 3),
            }

    preview = None
    if crop is not None:
        ok, buf = cv2.imencode(".jpg", crop)
        if ok:
            preview = "data:image/jpeg;base64," + base64.b64encode(buf.tobytes()).decode()

    capture_token = uuid.uuid4().hex
    with _pending_lock:
        _pending[capture_token] = {
            "embeddings": embeddings,
            "crop": crop,
            "class_id": class_id,
            "ts": time.time(),
        }

    return jsonify(
        {
            "ok": True,
            "token": capture_token,
            "samples": int(embeddings.shape[0]),
            "preview": preview,
            "duplicate": duplicate,
        }
    )


def _enroll_payload() -> dict:
    if request.is_json:
        data = request.get_json(silent=True) or {}
        return {
            "token": (data.get("token") or "").strip(),
            "name": (data.get("name") or "").strip(),
            "roll_no": (data.get("roll_no") or "").strip(),
            "email": (data.get("email") or "").strip(),
            "phone": (data.get("phone") or "").strip(),
            "external_id": (data.get("external_id") or "").strip(),
        }
    return {
        "token": (request.form.get("token") or "").strip(),
        "name": (request.form.get("name") or "").strip(),
        "roll_no": (request.form.get("roll_no") or "").strip(),
        "email": (request.form.get("email") or "").strip(),
        "phone": (request.form.get("phone") or "").strip(),
        "external_id": (request.form.get("external_id") or "").strip(),
    }


@app.route("/classes/<int:class_id>/students", methods=["POST"])
@api_key_or_login_required
def create_student(class_id: int):
    if session.get("admin_id") is not None:
        _owned_class_or_404(class_id)
    else:
        _class_or_404(class_id)

    payload = _enroll_payload()
    token = payload["token"]
    name = payload["name"]
    roll_no = payload["roll_no"]
    email = payload["email"]
    phone = payload["phone"]
    external_id = payload["external_id"]

    with _pending_lock:
        pending = _pending.get(token)

    if pending is None or pending["class_id"] != class_id:
        return jsonify(
            {"ok": False, "error": "Face capture expired. Please capture again."}
        ), 400
    if not name or not roll_no:
        return jsonify({"ok": False, "error": "Name and roll number are required."}), 400

    existing = None
    if external_id:
        existing = database.get_student_by_external_id(external_id)
        if existing is not None and existing["class_id"] != class_id:
            return jsonify(
                {"ok": False, "error": "External id belongs to a different class."}
            ), 400
    if existing is None:
        existing = database.get_student_by_roll(class_id, roll_no)

    if existing is not None:
        student_id = int(existing["id"])
        database.upsert_student_by_external(
            class_id, external_id or existing["external_id"] or "", name, roll_no, email, phone
        )
    else:
        student_id = database.upsert_student_by_external(
            class_id, external_id, name, roll_no, email, phone
        )

    engine.save_student_embeddings(student_id, pending["embeddings"], pending["crop"])

    with _pending_lock:
        _pending.pop(token, None)

    redirect_url = url_for("class_detail", class_id=class_id)
    return jsonify(
        {
            "ok": True,
            "message": f"Enrolled {name} ({roll_no}).",
            "frm_student_id": student_id,
            "redirect": redirect_url,
        }
    )


@app.route("/api/v1/classes/<int:class_id>/enroll-from-url", methods=["POST"])
@api_key_required
def enroll_from_url(class_id: int):
    """Enroll a student face from a photo URL (used by CSV/Excel import)."""
    _class_or_404(class_id)
    payload = request.get_json(silent=True) or {}
    photo_url = (payload.get("photo_url") or "").strip()
    name = (payload.get("name") or "").strip()
    roll_no = (payload.get("roll_no") or "").strip()
    email = (payload.get("email") or "").strip() or None
    phone = (payload.get("phone") or "").strip() or None
    external_id = (payload.get("external_id") or "").strip()

    if not photo_url or not name or not roll_no:
        return jsonify({"ok": False, "error": "photo_url, name, and roll_no are required."}), 400

    try:
        req = urllib.request.Request(
            photo_url,
            headers={"User-Agent": "MedCollege-FRModule/1.0"},
        )
        with urllib.request.urlopen(req, timeout=20) as resp:
            data = resp.read()
        arr = np.frombuffer(data, dtype=np.uint8)
        frame = cv2.imdecode(arr, cv2.IMREAD_COLOR)
        if frame is None:
            return jsonify({"ok": False, "error": "Could not decode image from photo URL."}), 400
    except Exception as exc:
        return jsonify({"ok": False, "error": f"Failed to download photo: {exc}"}), 400

    embeddings, crop = engine.embeddings_from_image(frame, repeats=max(3, config.ENROLL_SAMPLES))
    if embeddings is None:
        return jsonify({"ok": False, "error": "No face detected in the photo URL image."}), 400

    existing = None
    if external_id:
        existing = database.get_student_by_external_id(external_id)
        if existing is not None and existing["class_id"] != class_id:
            return jsonify(
                {"ok": False, "error": "External id belongs to a different class."}
            ), 400
    if existing is None:
        existing = database.get_student_by_roll(class_id, roll_no)

    if existing is not None:
        student_id = int(existing["id"])
        database.upsert_student_by_external(
            class_id, external_id or existing.get("external_id") or "", name, roll_no, email, phone
        )
    else:
        student_id = database.upsert_student_by_external(
            class_id, external_id, name, roll_no, email, phone
        )

    engine.save_student_embeddings(student_id, embeddings, crop)
    return jsonify(
        {
            "ok": True,
            "message": f"Enrolled {name} ({roll_no}) from photo URL.",
            "frm_student_id": student_id,
        }
    )


def _enroll_student_with_embeddings(
    class_id: int,
    name: str,
    roll_no: str,
    email: str | None,
    phone: str | None,
    external_id: str,
    embeddings,
    crop,
):
    existing = None
    if external_id:
        existing = database.get_student_by_external_id(external_id)
        if existing is not None and existing["class_id"] != class_id:
            return None, "External id belongs to a different class."
    if existing is None:
        existing = database.get_student_by_roll(class_id, roll_no)

    if existing is not None:
        student_id = int(existing["id"])
        database.upsert_student_by_external(
            class_id, external_id or existing.get("external_id") or "", name, roll_no, email, phone
        )
    else:
        student_id = database.upsert_student_by_external(
            class_id, external_id, name, roll_no, email, phone
        )

    engine.save_student_embeddings(student_id, embeddings, crop)
    return student_id, None


@app.route("/api/v1/classes/<int:class_id>/enroll-from-file", methods=["POST"])
@api_key_required
def enroll_from_file(class_id: int):
    """Enroll a student face from an uploaded image file (admin photo upload)."""
    _class_or_404(class_id)
    photo = request.files.get("photo")
    name = (request.form.get("name") or "").strip()
    roll_no = (request.form.get("roll_no") or "").strip()
    email = (request.form.get("email") or "").strip() or None
    phone = (request.form.get("phone") or "").strip() or None
    external_id = (request.form.get("external_id") or "").strip()

    if photo is None or not name or not roll_no:
        return jsonify({"ok": False, "error": "photo file, name, and roll_no are required."}), 400

    data = photo.read()
    arr = np.frombuffer(data, dtype=np.uint8)
    frame = cv2.imdecode(arr, cv2.IMREAD_COLOR)
    if frame is None:
        return jsonify({"ok": False, "error": "Could not decode uploaded image."}), 400

    embeddings, crop = engine.embeddings_from_image(frame, repeats=max(3, config.ENROLL_SAMPLES))
    if embeddings is None:
        return jsonify({"ok": False, "error": "No face detected in the uploaded photo."}), 400

    student_id, err = _enroll_student_with_embeddings(
        class_id, name, roll_no, email, phone, external_id, embeddings, crop
    )
    if err:
        return jsonify({"ok": False, "error": err}), 400

    return jsonify(
        {
            "ok": True,
            "message": f"Enrolled {name} ({roll_no}) from uploaded photo.",
            "frm_student_id": student_id,
        }
    )


@app.route("/reload", methods=["POST"])
@login_required
def reload_models():
    used = engine.reload()
    flash(f"Reloaded recognition index ({used} embeddings).", "success")
    return redirect(request.referrer or url_for("classes"))


# --------------------------------------------------------------------------- #
# ASP.NET sync + recognition URL APIs
# --------------------------------------------------------------------------- #
@app.route("/api/v1/sync/class", methods=["POST"])
@api_key_required
def api_sync_class():
    payload = request.get_json(silent=True) or {}
    external_id = (payload.get("external_id") or "").strip()
    name = (payload.get("name") or "").strip()
    code = (payload.get("code") or "").strip()
    students = payload.get("students") or []

    if not external_id or not name or not code:
        return jsonify({"ok": False, "error": "external_id, name, and code are required."}), 400

    admin_id = database.get_portal_admin_id()
    try:
        frm_class_id = database.upsert_class_by_external(external_id, name, code, admin_id)
    except Exception as exc:
        return jsonify({"ok": False, "error": f"Failed to upsert class: {exc}"}), 400

    synced_students = []
    for s in students:
        s_ext = (s.get("external_id") or "").strip()
        s_name = (s.get("name") or "").strip()
        roll_no = (s.get("roll_no") or "").strip()
        email = (s.get("email") or "").strip() or None
        phone = (s.get("phone") or "").strip() or None
        if not s_name or not roll_no:
            continue
        frm_student_id = database.upsert_student_by_external(
            frm_class_id, s_ext, s_name, roll_no, email, phone
        )
        synced_students.append({"external_id": s_ext, "frm_student_id": frm_student_id})

    return jsonify(
        {
            "ok": True,
            "frm_class_id": frm_class_id,
            "students": synced_students,
        }
    )


@app.route("/api/v1/classes/<external_id>/recognize-url", methods=["GET"])
@api_key_required
def api_recognize_url(external_id: str):
    cls = database.get_class_by_external_id(external_id)
    if cls is None:
        return jsonify({"ok": False, "error": "Class not found."}), 404
    class_id = int(cls["id"])
    token = make_recognition_token(class_id)
    return jsonify({"ok": True, "url": f"/recognize/{class_id}?token={token}"})


@app.route("/api/v1/camera/status", methods=["GET"])
@api_key_required
def api_camera_status():
    available = camera.is_available()
    source = camera.source
    return jsonify({
        "ok": True,
        "available": available,
        "source": str(source),
        "width": config.FRAME_WIDTH,
        "height": config.FRAME_HEIGHT,
    })


@app.route("/api/v1/students/<int:student_id>", methods=["DELETE"])
@api_key_required
def api_delete_student(student_id: int):
    """Remove a student + face embeddings (used when admin deletes in portal)."""
    row = database.get_student(student_id)
    if row is None:
        return jsonify({"ok": False, "error": "Student not found."}), 404
    engine.delete_student_data(student_id)
    database.delete_student(student_id)
    return jsonify({"ok": True, "deleted": student_id})


# --------------------------------------------------------------------------- #
# JSON API + export
# --------------------------------------------------------------------------- #
@app.route("/classes/<int:class_id>/api/attendance")
@login_or_token_required
def api_attendance(class_id: int):
    if session.get("admin_id") is not None:
        _owned_class_or_404(class_id)
    else:
        _class_or_404(class_id)
    return jsonify(database.dashboard_rows(class_id))


@app.route("/classes/<int:class_id>/api/sessions")
@login_or_token_required
def api_sessions(class_id: int):
    """Full IN/OUT session log for a class (every visit)."""
    if session.get("admin_id") is not None:
        _owned_class_or_404(class_id)
    else:
        _class_or_404(class_id)
    return jsonify(database.all_sessions_for_export(class_id))


@app.route("/classes/<int:class_id>/ai/ask", methods=["POST"])
@login_required
def ai_ask(class_id: int):
    _owned_class_or_404(class_id)
    payload = request.get_json(silent=True) or {}
    question = (payload.get("question") or request.form.get("question") or "").strip()
    result = ai_assistant.answer_question(class_id, question)
    return jsonify(result)


@app.route("/classes/<int:class_id>/ai/report", methods=["POST"])
@login_required
def ai_report(class_id: int):
    _owned_class_or_404(class_id)
    return jsonify(ai_assistant.generate_report(class_id))


@app.route("/classes/<int:class_id>/ai/students/<int:student_id>/report", methods=["POST"])
@login_required
def ai_student_report(class_id: int, student_id: int):
    _owned_class_or_404(class_id)
    return jsonify(ai_assistant.generate_student_report(class_id, student_id))


@app.route("/classes/<int:class_id>/export.csv")
@login_or_token_required
def export_csv(class_id: int):
    if session.get("admin_id") is not None:
        cls = _owned_class_or_404(class_id)
    else:
        cls = _class_or_404(class_id)
    rows = database.all_sessions_for_export(class_id)
    buf = io.StringIO()
    writer = csv.writer(buf)
    writer.writerow(
        ["Name", "Roll No", "Date", "Entry Time", "Exit Time", "Duration", "Status"]
    )
    for r in rows:
        writer.writerow(
            [r["name"], r["roll_no"], r["date"], r["entry_time"],
             r["exit_time"], r["duration"], r["status"]]
        )
    safe = cls["code"].replace("/", "-")
    filename = f"attendance_{safe}_{date.today().isoformat()}.csv"
    return Response(
        buf.getvalue(),
        mimetype="text/csv",
        headers={"Content-Disposition": f"attachment; filename={filename}"},
    )


if __name__ == "__main__":
    if config.HEADLESS_RECOGNIZE:
        def _headless_loop() -> None:
            """Optional faculty-free capture (enable with FRM_HEADLESS_RECOGNIZE=1)."""
            print("[FRM] Headless recognition enabled (FRM_HEADLESS_RECOGNIZE=1)")
            while True:
                try:
                    if _enrollment_pause.is_set():
                        time.sleep(0.3)
                        continue
                    classes = database.list_all_classes()
                    frame = camera.read()
                    if frame is None or not classes:
                        time.sleep(0.5)
                        continue
                    detections = engine.recognize(frame)
                    for cls in classes:
                        cid = int(cls["id"])
                        allowed = database.class_student_ids(cid)
                        for det in detections:
                            student_id = det["student_id"]
                            if student_id is None:
                                continue
                            if student_id in allowed:
                                manager.process(student_id, cid)
                            else:
                                _notify_portal_wrong_class(cid, student_id)
                    time.sleep(0.2)
                except Exception:
                    time.sleep(1.0)

        threading.Thread(target=_headless_loop, daemon=True).start()
    else:
        print("[FRM] Attendance marking only while Admin Face Recognition is open")

    app.run(host=config.HOST, port=config.PORT, debug=False, threaded=True)
