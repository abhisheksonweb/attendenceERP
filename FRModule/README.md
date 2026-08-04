# Face Attendance Web Module

An Attendance & Student Tracking System organized around **admins**
and **classes**. An admin registers an account, registers one or more classes,
and adds students to a class using a **face-first enrollment flow**: the camera
verifies the face first, then a popup collects the student's details. Afterwards,
each time an enrolled student passes under that class's camera the system toggles
their state - first appearance is an **entry**, the next is an **exit**, and so
on. A student can enter and leave many times a day; the last exit is their
leaving time for the day. Everything runs locally through a small Flask web app
with a live camera feed.

## Roles & flow

1. **Admin registers/logs in** (`/register`, `/login`).
2. **Admin registers a class** on the Classes home page; the creating admin is
   that class's admin.
3. **Class admin adds a student**: opens *Add Student*, clicks **Start Face
   Capture** (face verification). On success a **popup** appears to enter the
   student's name, roll number, email, and phone, which finalizes enrollment and
   saves the face embeddings for that class.
4. **Attendance** is logged per class: the class live feed recognizes that
   class's students from the camera and records entry/exit into class-scoped
   attendance logs.

## How it works

```
Webcam -> OpenCV DNN (YuNet detect + SFace embed) -> Entry/Exit toggle -> SQLite
                          |                                                 |
                          v                                                 v
                    MJPEG stream  ------------------------------->  Browser dashboard
```

- **Detection:** YuNet (`cv2.FaceDetectorYN`).
- **Recognition:** SFace (`cv2.FaceRecognizerSF`) 128-d embeddings compared with
  cosine similarity. This is a deep-learning recognizer, more accurate than the
  classic LBPH approach, and it still runs entirely offline on CPU.
- **Storage:** SQLite (`data/attendance.db`), one row per visit (session).
- **Entry/Exit:** each recognition flips the student's state. A short debounce
  stops a lingering face from flipping repeatedly.

## Features

- **Admin accounts** with login/logout (passwords hashed with Werkzeug).
- **Class registration**; each admin manages their own classes.
- **Face-first student enrollment**: verify the face, then a popup collects
  details (name, roll, email, phone). Duplicate faces are flagged.
- Live per-class recognition with on-frame name + IN/OUT overlay (faces from
  other classes are marked "Other class" and not logged).
- Multiple entries/exits per student per day (visit sessions), scoped per class.
- Per-student daily **time in class** and **average time per day**.
- **Delete student / delete class** (removes records + embeddings).
- **Per-class CSV export** of attendance sessions.

## Requirements

- Python 3.10+ (tested target: 3.14)
- A working webcam

## Setup

```bash
python -m venv .venv
.venv\Scripts\Activate.ps1     # Windows PowerShell
pip install -r requirements.txt
```

### Models

The DNN model files live in `models/`:

- `face_detection_yunet_2023mar.onnx`
- `face_recognition_sface_2021dec.onnx`

If they are missing, download them once:

```bash
python download_models.py
```

## Run

```bash
python app.py
```

Then open http://127.0.0.1:8000 in your browser.
(Port 5000 is often reserved on Windows; change `PORT` in `config.py` if needed.)

## Usage

1. Open the app, **Register** an admin account (or **Login**).
2. On the **Classes** page, register a class (name + optional code).
3. Open the class, click **+ Add Student**, then **Start Face Capture**. When the
   face is verified, fill in the popup form and **Save Student**.
4. Back on the class page, when an enrolled student is recognized their state
   toggles: entry -> exit -> entry ... Each visit is recorded and the table
   shows visits, first in, last out, time in class today, and average per day.
5. Use **Export CSV** for that class's session log, and **Delete** to remove a
   student or the whole class.

## Configuration

Key knobs live in `config.py`:

- `PORT` - web server port (default `8000`).
- `CAMERA_INDEX` - USB webcam index (default `0`).
- `CAMERA_SOURCE` - optional RTSP/HTTP URL for campus IP cameras
  (env `CAMERA_SOURCE=rtsp://user:pass@host/stream`). When set, overrides index.
- `SFACE_COSINE_THRESHOLD` - match cutoff (higher = stricter; default `0.363`).
- `TOGGLE_DEBOUNCE_SECONDS` - min seconds between a student's state toggles.
- `ENROLL_SAMPLES` - number of face samples captured per student.

## Project structure

- `app.py` - Flask app, admin auth, class + student routes, capture/commit flow,
  per-class MJPEG stream and CSV export.
- `config.py` - tunable settings + model/data paths.
- `face_engine.py` - YuNet detection, SFace embeddings, capture/verify/commit,
  recognition.
- `database.py` - SQLite schema (admins, classes, students, sessions) + queries.
- `attendance_logic.py` - debounce + entry/exit session toggle.
- `download_models.py` - fetches the ONNX models into `models/`.
- `templates/`, `static/` - web UI.
- `data/` - generated at runtime (embeddings, preview crops, database).

## Notes

- `data/` (embeddings + database) is created at runtime and is git-ignored.
- If you previously used the LBPH version, re-enroll students so embeddings are
  built for the new recognizer.
