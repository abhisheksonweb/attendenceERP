"""SQLite storage: admins, classes, students and per-visit attendance sessions.

An admin registers (owns) one or more classes and acts as that class's admin.
Each student belongs to exactly one class. A student can enter and leave the
class many times per day; each visit is a `sessions` row scoped to the class:
`entry_ts` is set on entry, `exit_ts` on the matching exit. An open session
(exit_ts NULL) means the student is currently in class.
"""
from __future__ import annotations

import sqlite3
from datetime import date, datetime
from typing import Optional

from werkzeug.security import generate_password_hash

import config


def _connect() -> sqlite3.Connection:
    conn = sqlite3.connect(config.DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    return conn


def _columns(conn: sqlite3.Connection, table: str) -> set[str]:
    return {r["name"] for r in conn.execute(f"PRAGMA table_info({table})").fetchall()}


def init_db() -> None:
    config.ensure_dirs()
    with _connect() as conn:
        conn.executescript(
            """
            CREATE TABLE IF NOT EXISTS admins (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                username      TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                created_at    TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS classes (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                name       TEXT NOT NULL,
                code       TEXT NOT NULL UNIQUE,
                admin_id   INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (admin_id) REFERENCES admins (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS students (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                class_id   INTEGER,
                name       TEXT NOT NULL,
                roll_no    TEXT NOT NULL,
                email      TEXT,
                phone      TEXT,
                created_at TEXT NOT NULL,
                FOREIGN KEY (class_id) REFERENCES classes (id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS sessions (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                student_id INTEGER NOT NULL,
                class_id   INTEGER,
                date       TEXT NOT NULL,
                entry_ts   TEXT NOT NULL,
                exit_ts    TEXT,
                FOREIGN KEY (student_id) REFERENCES students (id) ON DELETE CASCADE,
                FOREIGN KEY (class_id) REFERENCES classes (id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_sessions_student_date
                ON sessions (student_id, date);
            """
        )
        _migrate(conn)
        _ensure_portal_admin(conn)
        # Indexes that depend on migrated columns are created afterwards.
        conn.executescript(
            """
            CREATE INDEX IF NOT EXISTS idx_sessions_class_date
                ON sessions (class_id, date);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_students_class_roll
                ON students (class_id, roll_no);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_classes_external_id
                ON classes (external_id) WHERE external_id IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS idx_students_external_id
                ON students (external_id) WHERE external_id IS NOT NULL;
            """
        )


def _migrate(conn: sqlite3.Connection) -> None:
    """Add columns introduced after the original two-table schema."""
    student_cols = _columns(conn, "students")
    if "class_id" not in student_cols:
        conn.execute("ALTER TABLE students ADD COLUMN class_id INTEGER")
    if "email" not in student_cols:
        conn.execute("ALTER TABLE students ADD COLUMN email TEXT")
    if "phone" not in student_cols:
        conn.execute("ALTER TABLE students ADD COLUMN phone TEXT")

    session_cols = _columns(conn, "sessions")
    if "class_id" not in session_cols:
        conn.execute("ALTER TABLE sessions ADD COLUMN class_id INTEGER")

    class_cols = _columns(conn, "classes")
    if "external_id" not in class_cols:
        conn.execute("ALTER TABLE classes ADD COLUMN external_id TEXT")

    if "external_id" not in student_cols:
        conn.execute("ALTER TABLE students ADD COLUMN external_id TEXT")


def _ensure_portal_admin(conn: sqlite3.Connection) -> None:
    """Create a default portal admin when the database has no admins."""
    row = conn.execute("SELECT id FROM admins LIMIT 1").fetchone()
    if row is not None:
        return
    conn.execute(
        "INSERT INTO admins (username, password_hash, created_at) VALUES (?, ?, ?)",
        (
            "portal",
            generate_password_hash("Portal@123"),
            datetime.now().isoformat(timespec="seconds"),
        ),
    )


def get_portal_admin_id() -> int:
    """Return the portal admin id used for ASP.NET-synced classes."""
    with _connect() as conn:
        row = conn.execute(
            "SELECT id FROM admins WHERE username = ?", ("portal",)
        ).fetchone()
        if row is not None:
            return int(row["id"])
        row = conn.execute("SELECT id FROM admins ORDER BY id ASC LIMIT 1").fetchone()
        if row is None:
            raise RuntimeError("No admin account exists.")
        return int(row["id"])


# --------------------------------------------------------------------------- #
# Helpers
# --------------------------------------------------------------------------- #
def _fmt_time(iso_ts: Optional[str]) -> str:
    if not iso_ts:
        return "-"
    return datetime.fromisoformat(iso_ts).strftime("%H:%M:%S")


def _duration_seconds(entry_ts: str, exit_ts: Optional[str], *, now: datetime) -> int:
    start = datetime.fromisoformat(entry_ts)
    end = datetime.fromisoformat(exit_ts) if exit_ts else now
    return max(0, int((end - start).total_seconds()))


def format_duration(seconds: int) -> str:
    """Seconds -> 'H:MM:SS'."""
    h, rem = divmod(int(seconds), 3600)
    m, s = divmod(rem, 60)
    return f"{h}:{m:02d}:{s:02d}"


# --------------------------------------------------------------------------- #
# Admins
# --------------------------------------------------------------------------- #
def add_admin(username: str, password_hash: str) -> int:
    with _connect() as conn:
        cur = conn.execute(
            "INSERT INTO admins (username, password_hash, created_at) VALUES (?, ?, ?)",
            (username, password_hash, datetime.now().isoformat(timespec="seconds")),
        )
        return int(cur.lastrowid)


def get_admin_by_username(username: str) -> Optional[sqlite3.Row]:
    with _connect() as conn:
        return conn.execute(
            "SELECT * FROM admins WHERE username = ?", (username,)
        ).fetchone()


def get_admin(admin_id: int) -> Optional[sqlite3.Row]:
    with _connect() as conn:
        return conn.execute(
            "SELECT * FROM admins WHERE id = ?", (admin_id,)
        ).fetchone()


# --------------------------------------------------------------------------- #
# Classes
# --------------------------------------------------------------------------- #
def add_class(name: str, code: str, admin_id: int) -> int:
    with _connect() as conn:
        cur = conn.execute(
            "INSERT INTO classes (name, code, admin_id, created_at) VALUES (?, ?, ?, ?)",
            (name, code, admin_id, datetime.now().isoformat(timespec="seconds")),
        )
        return int(cur.lastrowid)


def get_class(class_id: int) -> Optional[sqlite3.Row]:
    with _connect() as conn:
        return conn.execute(
            "SELECT * FROM classes WHERE id = ?", (class_id,)
        ).fetchone()


def list_classes(admin_id: int) -> list[dict]:
    with _connect() as conn:
        rows = conn.execute(
            """
            SELECT c.*, COUNT(s.id) AS student_count
            FROM classes c
            LEFT JOIN students s ON s.class_id = c.id
            WHERE c.admin_id = ?
            GROUP BY c.id
            ORDER BY c.created_at DESC
            """,
            (admin_id,),
        ).fetchall()
    return [dict(r) for r in rows]


def list_all_classes() -> list[dict]:
    with _connect() as conn:
        rows = conn.execute(
            """
            SELECT c.*, COUNT(s.id) AS student_count
            FROM classes c
            LEFT JOIN students s ON s.class_id = c.id
            GROUP BY c.id
            ORDER BY c.id ASC
            """
        ).fetchall()
    return [dict(r) for r in rows]


def delete_class(class_id: int) -> None:
    with _connect() as conn:
        conn.execute("DELETE FROM classes WHERE id = ?", (class_id,))


def get_class_by_external_id(external_id: str) -> Optional[sqlite3.Row]:
    with _connect() as conn:
        return conn.execute(
            "SELECT * FROM classes WHERE external_id = ?", (external_id,)
        ).fetchone()


def upsert_class_by_external(
    external_id: str, name: str, code: str, admin_id: int
) -> int:
    existing = get_class_by_external_id(external_id)
    now = datetime.now().isoformat(timespec="seconds")
    with _connect() as conn:
        if existing is not None:
            conn.execute(
                "UPDATE classes SET name = ?, code = ? WHERE id = ?",
                (name, code, existing["id"]),
            )
            return int(existing["id"])
        cur = conn.execute(
            """
            INSERT INTO classes (name, code, admin_id, created_at, external_id)
            VALUES (?, ?, ?, ?, ?)
            """,
            (name, code, admin_id, now, external_id),
        )
        return int(cur.lastrowid)


# --------------------------------------------------------------------------- #
# Students
# --------------------------------------------------------------------------- #
def add_student(
    class_id: int,
    name: str,
    roll_no: str,
    email: Optional[str] = None,
    phone: Optional[str] = None,
) -> int:
    with _connect() as conn:
        cur = conn.execute(
            """
            INSERT INTO students (class_id, name, roll_no, email, phone, created_at)
            VALUES (?, ?, ?, ?, ?, ?)
            """,
            (
                class_id,
                name,
                roll_no,
                email or None,
                phone or None,
                datetime.now().isoformat(timespec="seconds"),
            ),
        )
        return int(cur.lastrowid)


def get_student(student_id: int) -> Optional[sqlite3.Row]:
    with _connect() as conn:
        return conn.execute(
            "SELECT * FROM students WHERE id = ?", (student_id,)
        ).fetchone()


def get_student_by_roll(class_id: int, roll_no: str) -> Optional[sqlite3.Row]:
    with _connect() as conn:
        return conn.execute(
            "SELECT * FROM students WHERE class_id = ? AND roll_no = ?",
            (class_id, roll_no),
        ).fetchone()


def get_student_by_external_id(external_id: str) -> Optional[sqlite3.Row]:
    with _connect() as conn:
        return conn.execute(
            "SELECT * FROM students WHERE external_id = ?", (external_id,)
        ).fetchone()


def upsert_student_by_external(
    class_id: int,
    external_id: str,
    name: str,
    roll_no: str,
    email: Optional[str] = None,
    phone: Optional[str] = None,
) -> int:
    now = datetime.now().isoformat(timespec="seconds")
    existing = get_student_by_external_id(external_id) if external_id else None
    if existing is not None and existing["class_id"] == class_id:
        with _connect() as conn:
            conn.execute(
                """
                UPDATE students
                SET name = ?, roll_no = ?, email = ?, phone = ?
                WHERE id = ?
                """,
                (name, roll_no, email or None, phone or None, existing["id"]),
            )
        return int(existing["id"])

    by_roll = get_student_by_roll(class_id, roll_no)
    if by_roll is not None:
        with _connect() as conn:
            conn.execute(
                """
                UPDATE students
                SET name = ?, email = ?, phone = ?, external_id = ?
                WHERE id = ?
                """,
                (name, email or None, phone or None, external_id or None, by_roll["id"]),
            )
        return int(by_roll["id"])

    with _connect() as conn:
        cur = conn.execute(
            """
            INSERT INTO students
                (class_id, name, roll_no, email, phone, created_at, external_id)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            (class_id, name, roll_no, email or None, phone or None, now, external_id or None),
        )
        return int(cur.lastrowid)


def list_students(class_id: Optional[int] = None) -> list[sqlite3.Row]:
    with _connect() as conn:
        if class_id is None:
            return conn.execute(
                "SELECT * FROM students ORDER BY name COLLATE NOCASE"
            ).fetchall()
        return conn.execute(
            "SELECT * FROM students WHERE class_id = ? ORDER BY name COLLATE NOCASE",
            (class_id,),
        ).fetchall()


def class_student_ids(class_id: int) -> set[int]:
    with _connect() as conn:
        rows = conn.execute(
            "SELECT id FROM students WHERE class_id = ?", (class_id,)
        ).fetchall()
    return {r["id"] for r in rows}


def student_name_map(class_id: Optional[int] = None) -> dict[int, str]:
    return {
        row["id"]: f"{row['name']} ({row['roll_no']})"
        for row in list_students(class_id)
    }


def delete_student(student_id: int) -> None:
    with _connect() as conn:
        conn.execute("DELETE FROM students WHERE id = ?", (student_id,))


# --------------------------------------------------------------------------- #
# Sessions (entry / exit)
# --------------------------------------------------------------------------- #
def get_open_session(student_id: int, day: Optional[str] = None) -> Optional[sqlite3.Row]:
    day = day or date.today().isoformat()
    with _connect() as conn:
        return conn.execute(
            """
            SELECT * FROM sessions
            WHERE student_id = ? AND date = ? AND exit_ts IS NULL
            ORDER BY id DESC LIMIT 1
            """,
            (student_id, day),
        ).fetchone()


def open_session(
    student_id: int, class_id: Optional[int] = None, when: Optional[datetime] = None
) -> int:
    when = when or datetime.now()
    with _connect() as conn:
        cur = conn.execute(
            """
            INSERT INTO sessions (student_id, class_id, date, entry_ts)
            VALUES (?, ?, ?, ?)
            """,
            (
                student_id,
                class_id,
                when.date().isoformat(),
                when.isoformat(timespec="seconds"),
            ),
        )
        return int(cur.lastrowid)


def close_session(session_id: int, when: Optional[datetime] = None) -> None:
    when = when or datetime.now()
    with _connect() as conn:
        conn.execute(
            "UPDATE sessions SET exit_ts = ? WHERE id = ?",
            (when.isoformat(timespec="seconds"), session_id),
        )


# --------------------------------------------------------------------------- #
# Reporting
# --------------------------------------------------------------------------- #
def _student_averages(now: datetime, class_id: Optional[int] = None) -> dict[int, dict]:
    """Per student across all days: average daily time in class and totals."""
    with _connect() as conn:
        if class_id is None:
            rows = conn.execute(
                "SELECT student_id, date, entry_ts, exit_ts FROM sessions"
            ).fetchall()
        else:
            rows = conn.execute(
                "SELECT student_id, date, entry_ts, exit_ts FROM sessions WHERE class_id = ?",
                (class_id,),
            ).fetchall()

    per_student: dict[int, dict] = {}
    for r in rows:
        sid = r["student_id"]
        bucket = per_student.setdefault(sid, {"days": {}, "sessions": 0})
        bucket["sessions"] += 1
        day_seconds = bucket["days"].get(r["date"], 0)
        day_seconds += _duration_seconds(r["entry_ts"], r["exit_ts"], now=now)
        bucket["days"][r["date"]] = day_seconds

    result: dict[int, dict] = {}
    for sid, bucket in per_student.items():
        days = bucket["days"]
        n_days = len(days) or 1
        total = sum(days.values())
        result[sid] = {
            "days_attended": len(days),
            "total_sessions": bucket["sessions"],
            "avg_per_day_seconds": total // n_days,
            "avg_per_day": format_duration(total // n_days),
            "total_time": format_duration(total),
        }
    return result


def dashboard_rows(class_id: Optional[int] = None, day: Optional[str] = None) -> list[dict]:
    """Per-student summary for the given day, enriched with all-time averages."""
    now = datetime.now()
    day = day or date.today().isoformat()
    averages = _student_averages(now, class_id)

    query = """
        SELECT sn.student_id AS student_id, s.name AS name, s.roll_no AS roll_no,
               s.external_id AS external_id,
               sn.entry_ts AS entry_ts, sn.exit_ts AS exit_ts
        FROM sessions sn
        JOIN students s ON s.id = sn.student_id
        WHERE sn.date = ?
    """
    params: list = [day]
    if class_id is not None:
        query += " AND sn.class_id = ?"
        params.append(class_id)
    query += " ORDER BY sn.entry_ts ASC"

    with _connect() as conn:
        rows = conn.execute(query, params).fetchall()

    by_student: dict[int, dict] = {}
    for r in rows:
        sid = r["student_id"]
        agg = by_student.setdefault(
            sid,
            {
                "student_id": sid,
                "name": r["name"],
                "roll_no": r["roll_no"],
                "external_id": r["external_id"] or "",
                "sessions": 0,
                "first_in_ts": r["entry_ts"],
                "last_out_ts": None,
                "seconds": 0,
                "open": False,
            },
        )
        agg["sessions"] += 1
        agg["seconds"] += _duration_seconds(r["entry_ts"], r["exit_ts"], now=now)
        if r["exit_ts"]:
            if agg["last_out_ts"] is None or r["exit_ts"] > agg["last_out_ts"]:
                agg["last_out_ts"] = r["exit_ts"]
        else:
            agg["open"] = True

    result = []
    for sid, agg in by_student.items():
        avg = averages.get(sid, {})
        result.append(
            {
                "student_id": agg["student_id"],
                "name": agg["name"],
                "roll_no": agg["roll_no"],
                "external_id": agg.get("external_id") or "",
                "sessions": agg["sessions"],
                "first_in": _fmt_time(agg["first_in_ts"]),
                "last_out": _fmt_time(agg["last_out_ts"]),
                "time_in_class": format_duration(agg["seconds"]),
                "time_in_class_seconds": int(agg["seconds"]),
                "avg_per_day": avg.get("avg_per_day", "-"),
                "status": "IN" if agg["open"] else "OUT",
            }
        )
    result.sort(key=lambda x: (x["status"] != "IN", x["last_out"]), reverse=False)
    return result


def all_sessions_for_export(class_id: Optional[int] = None) -> list[dict]:
    """Every session with student info and duration, for CSV export."""
    now = datetime.now()
    query = """
        SELECT s.name AS name, s.roll_no AS roll_no, s.external_id AS external_id,
               sn.date AS date, sn.entry_ts AS entry_ts, sn.exit_ts AS exit_ts
        FROM sessions sn
        JOIN students s ON s.id = sn.student_id
    """
    params: list = []
    if class_id is not None:
        query += " WHERE sn.class_id = ?"
        params.append(class_id)
    query += " ORDER BY s.name COLLATE NOCASE, sn.entry_ts ASC"

    with _connect() as conn:
        rows = conn.execute(query, params).fetchall()

    export = []
    for r in rows:
        secs = _duration_seconds(r["entry_ts"], r["exit_ts"], now=now)
        export.append(
            {
                "name": r["name"],
                "roll_no": r["roll_no"],
                "external_id": r["external_id"] or "",
                "date": r["date"],
                "entry_ts": r["entry_ts"],
                "exit_ts": r["exit_ts"],
                "entry_time": _fmt_time(r["entry_ts"]),
                "exit_time": _fmt_time(r["exit_ts"]),
                "duration": format_duration(secs),
                "status": "IN" if not r["exit_ts"] else "OUT",
            }
        )
    return export


# --------------------------------------------------------------------------- #
# Attendance analytics (numeric aggregates grounding the assistant)
# --------------------------------------------------------------------------- #
def class_ai_stats(class_id: int) -> dict:
    """Structured numeric attendance stats for one class.

    Returns a dictionary the attendance assistant uses as trustworthy ground truth so
    the language model never has to invent figures:
      - students: per-student attendance aggregates
      - class_days: sorted list of distinct dates the class had any activity
      - daily_present: {date: distinct students present}
      - totals: headline counts
    """
    now = datetime.now()
    today = date.today().isoformat()

    students = list_students(class_id)
    student_meta = {
        s["id"]: {"name": s["name"], "roll_no": s["roll_no"]} for s in students
    }

    with _connect() as conn:
        rows = conn.execute(
            """
            SELECT student_id, date, entry_ts, exit_ts
            FROM sessions
            WHERE class_id = ?
            ORDER BY date ASC, entry_ts ASC
            """,
            (class_id,),
        ).fetchall()

    per_student: dict[int, dict] = {}
    daily_present: dict[str, set[int]] = {}
    class_days: set[str] = set()

    for r in rows:
        sid = r["student_id"]
        day = r["date"]
        class_days.add(day)
        daily_present.setdefault(day, set()).add(sid)

        bucket = per_student.setdefault(
            sid,
            {"days": {}, "sessions": 0, "first_date": day, "last_date": day, "open": False},
        )
        bucket["sessions"] += 1
        bucket["days"][day] = bucket["days"].get(day, 0) + _duration_seconds(
            r["entry_ts"], r["exit_ts"], now=now
        )
        bucket["first_date"] = min(bucket["first_date"], day)
        bucket["last_date"] = max(bucket["last_date"], day)
        if not r["exit_ts"] and day == today:
            bucket["open"] = True

    total_class_days = len(class_days) or 1

    student_stats: list[dict] = []
    for sid, meta in student_meta.items():
        b = per_student.get(sid)
        if b is None:
            student_stats.append(
                {
                    "name": meta["name"],
                    "roll_no": meta["roll_no"],
                    "days_attended": 0,
                    "total_sessions": 0,
                    "total_seconds": 0,
                    "total_time": format_duration(0),
                    "avg_seconds_per_day": 0,
                    "avg_per_day": format_duration(0),
                    "attendance_rate": 0.0,
                    "first_date": None,
                    "last_date": None,
                    "present_today": False,
                    "in_class_now": False,
                }
            )
            continue
        days = b["days"]
        n_days = len(days)
        total = sum(days.values())
        avg = total // (n_days or 1)
        student_stats.append(
            {
                "name": meta["name"],
                "roll_no": meta["roll_no"],
                "days_attended": n_days,
                "total_sessions": b["sessions"],
                "total_seconds": total,
                "total_time": format_duration(total),
                "avg_seconds_per_day": avg,
                "avg_per_day": format_duration(avg),
                "attendance_rate": round(n_days / total_class_days, 3),
                "first_date": b["first_date"],
                "last_date": b["last_date"],
                "present_today": today in days,
                "in_class_now": b["open"],
            }
        )

    student_stats.sort(key=lambda x: x["name"].lower())

    present_today_ids = daily_present.get(today, set())
    return {
        "students": student_stats,
        "class_days": sorted(class_days),
        "total_class_days": len(class_days),
        "daily_present": {d: len(ids) for d, ids in sorted(daily_present.items())},
        "totals": {
            "enrolled": len(students),
            "present_today": len(present_today_ids),
            "absent_today": len(students) - len(present_today_ids),
            "in_class_now": sum(1 for s in student_stats if s["in_class_now"]),
        },
        "today": today,
    }


def student_ai_detail(class_id: int, student_id: int) -> Optional[dict]:
    """Detailed attendance breakdown for one student in one class.

    Includes the specific dates the student attended and, by comparing against
    all dates the class met, the exact dates they missed. Returns None if the
    student does not belong to the class.
    """
    now = datetime.now()
    today = date.today().isoformat()

    student = get_student(student_id)
    if student is None or student["class_id"] != class_id:
        return None

    with _connect() as conn:
        class_days = [
            r["date"]
            for r in conn.execute(
                "SELECT DISTINCT date FROM sessions WHERE class_id = ? ORDER BY date ASC",
                (class_id,),
            ).fetchall()
        ]
        rows = conn.execute(
            """
            SELECT date, entry_ts, exit_ts FROM sessions
            WHERE class_id = ? AND student_id = ?
            ORDER BY date ASC, entry_ts ASC
            """,
            (class_id, student_id),
        ).fetchall()

    per_day: dict[str, int] = {}
    sessions = 0
    in_class_now = False
    for r in rows:
        sessions += 1
        per_day[r["date"]] = per_day.get(r["date"], 0) + _duration_seconds(
            r["entry_ts"], r["exit_ts"], now=now
        )
        if not r["exit_ts"] and r["date"] == today:
            in_class_now = True

    attended = sorted(per_day.keys())
    missed = [d for d in class_days if d not in per_day]
    total_class_days = len(class_days) or 1
    total_seconds = sum(per_day.values())
    n_days = len(attended)
    avg = total_seconds // (n_days or 1)

    return {
        "name": student["name"],
        "roll_no": student["roll_no"],
        "email": student["email"],
        "phone": student["phone"],
        "total_class_days": len(class_days),
        "days_attended": n_days,
        "days_missed": len(missed),
        "attendance_rate": round(n_days / total_class_days, 3),
        "attended_dates": attended,
        "missed_dates": missed,
        "total_sessions": sessions,
        "total_time": format_duration(total_seconds),
        "avg_per_day": format_duration(avg),
        "present_today": today in per_day,
        "in_class_now": in_class_now,
        "last_seen": attended[-1] if attended else None,
        "daily_seconds": {d: per_day[d] for d in attended},
        "today": today,
    }
