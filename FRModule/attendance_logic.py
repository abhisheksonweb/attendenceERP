"""Turns a recognized student into an entry/exit event.

Modes:
  - "in"  (check-in camera): always open a session if the student is OUT.
            Already-IN students stay IN (no exit).
  - "out" (check-out camera): always close the open session if the student is IN.
            Already-OUT students stay OUT (no entry).
  - None / "toggle" (legacy): each recognition flips IN <-> OUT.

A per-student debounce prevents a lingering face from firing repeatedly.
"""
from __future__ import annotations

import threading
from datetime import datetime

import config
import database


class AttendanceManager:
    def __init__(self, debounce_seconds: int = config.TOGGLE_DEBOUNCE_SECONDS):
        self.debounce = debounce_seconds
        self._last_toggle: dict[int, datetime] = {}
        self._lock = threading.Lock()

    def _in_cooldown(self, student_id: int, now: datetime) -> bool:
        last = self._last_toggle.get(student_id)
        return last is not None and (now - last).total_seconds() < self.debounce

    def status_for(self, student_id: int) -> str:
        """'IN' if a session is currently open, else 'OUT'."""
        return "IN" if database.get_open_session(student_id) else "OUT"

    def process(
        self,
        student_id: int,
        class_id: int | None = None,
        mode: str | None = None,
    ) -> dict:
        """Apply debounce + check-in/check-out (or legacy toggle).

        mode: "in" | "out" | "toggle" | None
          - "in":  check-in only (open session if currently OUT)
          - "out": check-out only (close session if currently IN)
          - "toggle"/None: flip state (legacy)

        Returns {'action', 'status'} where action is 'entry', 'exit', or None
        and status is 'IN' or 'OUT'.
        """
        normalized = (mode or "toggle").strip().lower()
        if normalized in ("checkin", "check-in", "entry"):
            normalized = "in"
        elif normalized in ("checkout", "check-out", "exit"):
            normalized = "out"
        elif normalized not in ("in", "out", "toggle"):
            normalized = "toggle"

        now = datetime.now()
        with self._lock:
            open_session = database.get_open_session(student_id)

            if self._in_cooldown(student_id, now):
                return {"action": None, "status": "IN" if open_session else "OUT"}

            if normalized == "in":
                if open_session is None:
                    database.open_session(student_id, class_id, now)
                    self._last_toggle[student_id] = now
                    return {"action": "entry", "status": "IN"}
                return {"action": None, "status": "IN"}

            if normalized == "out":
                if open_session is not None:
                    database.close_session(open_session["id"], now)
                    self._last_toggle[student_id] = now
                    return {"action": "exit", "status": "OUT"}
                return {"action": None, "status": "OUT"}

            # Legacy toggle
            if open_session is None:
                database.open_session(student_id, class_id, now)
                self._last_toggle[student_id] = now
                return {"action": "entry", "status": "IN"}

            database.close_session(open_session["id"], now)
            self._last_toggle[student_id] = now
            return {"action": "exit", "status": "OUT"}
