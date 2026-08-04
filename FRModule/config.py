"""Central configuration for the Face Attendance Web Module."""
import os
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent
DATA_DIR = BASE_DIR / "data"
FACES_DIR = DATA_DIR / "faces"            # legacy; face PNGs are no longer stored
EMBEDDINGS_DIR = DATA_DIR / "embeddings"  # SFace embeddings per student (.npy)
DB_PATH = DATA_DIR / "attendance.db"

# Bundled DNN models live in the repo (downloaded once from OpenCV Zoo).
MODELS_DIR = BASE_DIR / "models"
YUNET_PATH = MODELS_DIR / "face_detection_yunet_2023mar.onnx"
SFACE_PATH = MODELS_DIR / "face_recognition_sface_2021dec.onnx"

# Web server host/port. Port 5000 is often reserved on Windows, so default 8000.
HOST = "127.0.0.1"
PORT = 8000

# Camera source for live campus use:
# - Leave CAMERA_SOURCE empty and set CAMERA_INDEX for a USB webcam (default).
# - Or set CAMERA_SOURCE to an RTSP/HTTP URL, e.g.
#     set CAMERA_SOURCE=rtsp://user:pass@192.168.1.50:554/stream
CAMERA_SOURCE = os.environ.get("CAMERA_SOURCE", "").strip()
CAMERA_INDEX = int(os.environ.get("CAMERA_INDEX", "0"))

# Frame size used for the stream (kept modest for responsiveness).
FRAME_WIDTH = 640
FRAME_HEIGHT = 480

# YuNet face detector thresholds.
YUNET_SCORE_THRESHOLD = 0.8
YUNET_NMS_THRESHOLD = 0.3
YUNET_TOP_K = 5000

# SFace embeddings are compared with cosine similarity: HIGHER = more similar.
# 0.363 is OpenCV's recommended same-identity cutoff for this model.
SFACE_COSINE_THRESHOLD = 0.363

# Minimum seconds before the same student's state can toggle again (prevents a
# lingering face from flipping entry/exit repeatedly).
TOGGLE_DEBOUNCE_SECONDS = 15

# Number of face samples captured during enrollment.
ENROLL_SAMPLES = 20

# --------------------------------------------------------------------------- #
# Attendance assistant (optional LLM layer)
# --------------------------------------------------------------------------- #
# The assistant works entirely offline with a built-in rule-based engine.
# To upgrade answers/reports with a language model, set these environment
# variables (any OpenAI-compatible endpoint works: OpenAI, Groq, Ollama, etc.):
#   AI_API_KEY   - API key / token (leave empty to stay fully offline)
#   AI_BASE_URL  - Chat-completions base URL (default: OpenAI)
#   AI_MODEL     - Model name to use
# Example for a local Ollama server:
#   AI_BASE_URL=http://localhost:11434/v1  AI_MODEL=llama3.1  AI_API_KEY=ollama
AI_API_KEY = os.environ.get("AI_API_KEY", "").strip()
AI_BASE_URL = os.environ.get("AI_BASE_URL", "https://api.openai.com/v1").rstrip("/")
AI_MODEL = os.environ.get("AI_MODEL", "gpt-4o-mini").strip()
AI_TIMEOUT_SECONDS = 30

# ASP.NET portal integration
API_KEY = os.environ.get("FRM_API_KEY", "medcollege-frm-key")
ASPNET_CORS_ORIGINS = ["http://localhost:5148", "http://127.0.0.1:5148"]
PORTAL_BASE_URL = os.environ.get("PORTAL_BASE_URL", "http://127.0.0.1:5148").rstrip("/")
WRONG_CLASS_COOLDOWN_SECONDS = int(os.environ.get("WRONG_CLASS_COOLDOWN_SECONDS", "300"))
# When true, run recognition in background without keeping an admin browser tab open.
# Default OFF: attendance is marked only while Admin opens Face Recognition for a class.
# Face enrollment / capture must never mark attendance.
HEADLESS_RECOGNIZE = os.environ.get("FRM_HEADLESS_RECOGNIZE", "0").strip() in ("1", "true", "True", "yes")


def ai_enabled() -> bool:
    """True when an LLM endpoint is configured; otherwise the local engine runs."""
    return bool(AI_API_KEY)


def ensure_dirs() -> None:
    """Create the runtime data directories if they do not yet exist."""
    for path in (DATA_DIR, FACES_DIR, EMBEDDINGS_DIR):
        path.mkdir(parents=True, exist_ok=True)
