"""Attendance assistant for the face attendance module.

Two capabilities are exposed:
  * answer_question(class_id, question) -> natural-language Q&A
  * generate_report(class_id)          -> written attendance summary

Design notes
------------
Answers are *grounded*: all figures come from ``database.class_ai_stats`` (real
SQL aggregates), never from the language model's imagination. The LLM only
phrases the pre-computed facts. If no LLM endpoint is configured (see
``config.ai_enabled``), a deterministic rule-based engine produces answers so
the feature works fully offline. The LLM call uses only the standard library
(``urllib``) and targets any OpenAI-compatible ``/chat/completions`` endpoint.
"""
from __future__ import annotations

import json
import re
import urllib.error
import urllib.request

import config
import database

# Attendance below this share of class days flags a chronic-absence concern.
CHRONIC_RATE = 0.5

EXAMPLE_QUESTIONS = [
    "Who was absent today?",
    "Which students have the lowest attendance?",
    "Who is in class right now?",
    "Who has perfect attendance?",
    "Give me a summary of this class.",
]


# --------------------------------------------------------------------------- #
# Context / derived insights
# --------------------------------------------------------------------------- #
def _derive(stats: dict) -> dict:
    """Turn raw stats into ranked, human-meaningful groupings."""
    students = stats["students"]
    total_days = stats["total_class_days"]

    present_today = [s for s in students if s["present_today"]]
    absent_today = [s for s in students if not s["present_today"]]
    in_class_now = [s for s in students if s["in_class_now"]]

    ranked = sorted(students, key=lambda s: s["attendance_rate"], reverse=True)
    chronic = [
        s for s in students
        if total_days > 0 and s["attendance_rate"] < CHRONIC_RATE
    ]
    perfect = [
        s for s in students
        if total_days > 0 and s["days_attended"] == total_days
    ]

    never_seen = [s for s in students if s["days_attended"] == 0]

    return {
        "present_today": present_today,
        "absent_today": absent_today,
        "in_class_now": in_class_now,
        "ranked_by_attendance": ranked,
        "chronic_absentees": chronic,
        "perfect_attendance": perfect,
        "never_seen": never_seen,
    }


def build_context(class_id: int) -> tuple[dict, dict]:
    """Return (raw_stats, derived_insights) for a class."""
    stats = database.class_ai_stats(class_id)
    return stats, _derive(stats)


# --------------------------------------------------------------------------- #
# Formatting helpers (shared by local engine and LLM prompt)
# --------------------------------------------------------------------------- #
def _names(items: list[dict], limit: int = 20) -> str:
    if not items:
        return "none"
    shown = [f"{s['name']} ({s['roll_no']})" for s in items[:limit]]
    extra = len(items) - len(shown)
    if extra > 0:
        shown.append(f"and {extra} more")
    return ", ".join(shown)


def _pct(rate: float) -> str:
    return f"{round(rate * 100)}%"


# --------------------------------------------------------------------------- #
# Local (offline) engine
# --------------------------------------------------------------------------- #
def _local_answer(question: str, stats: dict, derived: dict) -> str:
    q = question.lower().strip()
    totals = stats["totals"]
    days = stats["total_class_days"]

    def line(label, items):
        return f"{label} ({len(items)}): {_names(items)}"

    if any(k in q for k in ("in class now", "right now", "currently", "present now")):
        return line("Currently in class", derived["in_class_now"])

    if "absent" in q or "missing" in q or "not here" in q or "who is not" in q:
        return line(f"Absent today ({stats['today']})", derived["absent_today"])

    if "present" in q or ("here" in q and "today" in q) or "attended today" in q:
        return line(f"Present today ({stats['today']})", derived["present_today"])

    if "perfect" in q or ("100" in q and "attend" in q):
        return line("Perfect attendance", derived["perfect_attendance"])

    if "never" in q or "not enrolled attend" in q or "no attendance" in q:
        return line("Never recorded attending", derived["never_seen"])

    if any(k in q for k in ("lowest", "worst", "least", "chronic", "risk", "poor")):
        chronic = derived["chronic_absentees"]
        if not chronic:
            worst = derived["ranked_by_attendance"][-5:][::-1]
            return "No student is below 50% attendance. Lowest few: " + ", ".join(
                f"{s['name']} {_pct(s['attendance_rate'])}" for s in worst
            )
        return "Chronic absentees (<50% of days): " + ", ".join(
            f"{s['name']} {_pct(s['attendance_rate'])}" for s in chronic
        )

    if any(k in q for k in ("highest", "best", "most", "top")):
        top = derived["ranked_by_attendance"][:5]
        return "Highest attendance: " + ", ".join(
            f"{s['name']} {_pct(s['attendance_rate'])}" for s in top
        )

    if "how many" in q or "count" in q or "number of" in q:
        return (
            f"{totals['enrolled']} students enrolled. Today: "
            f"{totals['present_today']} present, {totals['absent_today']} absent, "
            f"{totals['in_class_now']} currently in class. "
            f"The class has met on {days} day(s)."
        )

    if "average" in q or "avg" in q or "time" in q or "duration" in q or "long" in q:
        rows = sorted(
            stats["students"], key=lambda s: s["avg_seconds_per_day"], reverse=True
        )[:5]
        if not rows:
            return "No attendance recorded yet."
        return "Average time in class per day (top 5): " + ", ".join(
            f"{s['name']} {s['avg_per_day']}" for s in rows
        )

    if "summary" in q or "report" in q or "overview" in q or "how is" in q:
        return _local_report(stats, derived)

    # Fallback overview.
    return (
        f"I can answer questions about this class's attendance. "
        f"Right now: {totals['enrolled']} enrolled, {totals['present_today']} present today, "
        f"{totals['absent_today']} absent, {totals['in_class_now']} in class now. "
        f"Try asking: {EXAMPLE_QUESTIONS[0]!r} or {EXAMPLE_QUESTIONS[1]!r}."
    )


def _local_report(stats: dict, derived: dict) -> str:
    totals = stats["totals"]
    days = stats["total_class_days"]
    lines = [
        f"Attendance summary ({stats['today']})",
        f"- Enrolled: {totals['enrolled']} | Class days on record: {days}",
        f"- Today: {totals['present_today']} present, {totals['absent_today']} absent, "
        f"{totals['in_class_now']} currently in class.",
    ]
    if derived["perfect_attendance"]:
        lines.append(f"- Perfect attendance: {_names(derived['perfect_attendance'])}.")
    if derived["chronic_absentees"]:
        lines.append(
            "- Needs attention (<50% attendance): "
            + ", ".join(
                f"{s['name']} {_pct(s['attendance_rate'])}"
                for s in derived["chronic_absentees"]
            )
            + "."
        )
    else:
        lines.append("- No chronic absentees below 50%.")
    if derived["never_seen"]:
        lines.append(f"- Enrolled but never recorded: {_names(derived['never_seen'])}.")
    return "\n".join(lines)


# --------------------------------------------------------------------------- #
# Per-student lookup + report
# --------------------------------------------------------------------------- #
_STUDENT_STOPWORDS = {
    "report", "for", "the", "student", "generate", "give", "me", "on", "about",
    "of", "roll", "no", "number", "attendance", "a", "an", "please", "show",
    "how", "is", "are", "doing", "details", "detail", "profile", "and", "then",
}


def find_student(class_id: int, text: str):
    """Best-effort match of a class student from free text (roll no or name)."""
    text_l = (text or "").lower()
    tokens = re.findall(r"[a-z0-9]+", text_l)
    token_set = set(tokens)
    students = database.list_students(class_id)

    # 1) Exact roll number token match (most reliable).
    for s in students:
        if s["roll_no"] and s["roll_no"].lower() in token_set:
            return s["id"]

    # 2) Full name appears as a substring.
    for s in students:
        if s["name"] and s["name"].lower() in text_l:
            return s["id"]

    # 3) First name (or any name part) matches a non-stopword token.
    for s in students:
        for part in (s["name"] or "").lower().split():
            if len(part) > 2 and part not in _STUDENT_STOPWORDS and part in token_set:
                return s["id"]
    return None


def _local_student_report(d: dict) -> str:
    rate = _pct(d["attendance_rate"])
    lines = [
        f"Attendance report — {d['name']} ({d['roll_no']})",
        f"- Attendance: {d['days_attended']}/{d['total_class_days']} class days ({rate}).",
        f"- Status today ({d['today']}): "
        + ("in class now" if d["in_class_now"]
           else "present" if d["present_today"] else "absent")
        + ".",
        f"- Total visits: {d['total_sessions']} | Total time: {d['total_time']} | "
        f"Avg/day: {d['avg_per_day']}.",
        f"- Last seen: {d['last_seen'] or 'never recorded'}.",
    ]
    if d["missed_dates"]:
        shown = ", ".join(d["missed_dates"][:10])
        if d["days_missed"] > 10:
            shown += f", and {d['days_missed'] - 10} more"
        lines.append(f"- Missed {d['days_missed']} day(s): {shown}.")
    else:
        lines.append("- No missed class days on record.")

    if d["attendance_rate"] < CHRONIC_RATE and d["total_class_days"] > 0:
        lines.append(
            "- Concern: attendance is below 50%. Consider following up with the "
            "student"
            + (f" ({d['email']})" if d["email"] else "")
            + "."
        )
    return "\n".join(lines)


_STUDENT_SYSTEM = (
    "You are an attendance analytics assistant. You are given verified attendance "
    "facts for a SINGLE student as JSON. Write a short, professional report for the "
    "class admin about this student only. Answer strictly from the facts; never "
    "invent dates or numbers. Cover attendance rate, days missed, recent activity, "
    "and (if attendance is low) a brief, polite follow-up suggestion. Keep it under "
    "150 words."
)


def generate_student_report(class_id: int, student_id: int) -> dict:
    detail = database.student_ai_detail(class_id, student_id)
    if detail is None:
        return {"report": "Student not found in this class.", "source": "local"}

    if config.ai_enabled():
        try:
            facts = json.dumps(detail, ensure_ascii=False)
            prompt = f"Student attendance facts (JSON):\n{facts}\n\nWrite the report."
            return {
                "report": _call_llm(_STUDENT_SYSTEM, prompt),
                "source": "ai",
                "student": {"name": detail["name"], "roll_no": detail["roll_no"]},
            }
        except (urllib.error.URLError, urllib.error.HTTPError, KeyError, TimeoutError, ValueError):
            pass

    return {
        "report": _local_student_report(detail),
        "source": "local",
        "student": {"name": detail["name"], "roll_no": detail["roll_no"]},
    }


# --------------------------------------------------------------------------- #
# LLM engine (OpenAI-compatible, stdlib only)
# --------------------------------------------------------------------------- #
def _facts_payload(stats: dict, derived: dict) -> str:
    """Compact JSON of ground-truth facts handed to the model."""
    compact = {
        "today": stats["today"],
        "totals": stats["totals"],
        "total_class_days": stats["total_class_days"],
        "daily_present_counts": stats["daily_present"],
        "students": [
            {
                "name": s["name"],
                "roll_no": s["roll_no"],
                "days_attended": s["days_attended"],
                "attendance_rate": s["attendance_rate"],
                "avg_time_per_day": s["avg_per_day"],
                "total_time": s["total_time"],
                "present_today": s["present_today"],
                "in_class_now": s["in_class_now"],
                "last_date": s["last_date"],
            }
            for s in stats["students"]
        ],
        "highlights": {
            "chronic_absentees": [s["name"] for s in derived["chronic_absentees"]],
            "perfect_attendance": [s["name"] for s in derived["perfect_attendance"]],
            "absent_today": [s["name"] for s in derived["absent_today"]],
            "in_class_now": [s["name"] for s in derived["in_class_now"]],
        },
    }
    return json.dumps(compact, ensure_ascii=False)


def _call_llm(system_prompt: str, user_prompt: str) -> str:
    body = json.dumps(
        {
            "model": config.AI_MODEL,
            "temperature": 0.2,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt},
            ],
        }
    ).encode("utf-8")

    req = urllib.request.Request(
        f"{config.AI_BASE_URL}/chat/completions",
        data=body,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {config.AI_API_KEY}",
        },
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=config.AI_TIMEOUT_SECONDS) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    return data["choices"][0]["message"]["content"].strip()


_SYSTEM = (
    "You are an attendance analytics assistant for a class. You are given a JSON "
    "object of verified attendance facts. Answer ONLY from these facts. Never "
    "invent names, numbers, or dates. If the facts do not contain the answer, say "
    "so briefly. Be concise and specific; prefer short sentences or compact lists. "
    "Refer to students by name and roll number."
)


# --------------------------------------------------------------------------- #
# Public API
# --------------------------------------------------------------------------- #
def answer_question(class_id: int, question: str) -> dict:
    question = (question or "").strip()
    if not question:
        return {"answer": "Please type a question.", "source": "local"}

    stats, derived = build_context(class_id)
    if stats["totals"]["enrolled"] == 0:
        return {
            "answer": "No students are enrolled in this class yet, so there is no "
            "attendance data to analyze.",
            "source": "local",
        }

    # Route "report for <student>" style questions to a per-student report,
    # provided the question actually names a specific student.
    q = question.lower()
    wants_report = any(
        k in q for k in ("report", "profile", "details", "how is", "how's", "doing", "summary")
    )
    student_id = find_student(class_id, question)
    if student_id is not None and wants_report:
        result = generate_student_report(class_id, student_id)
        return {"answer": result["report"], "source": result["source"]}

    if config.ai_enabled():
        try:
            facts = _facts_payload(stats, derived)
            prompt = f"Attendance facts (JSON):\n{facts}\n\nQuestion: {question}"
            return {"answer": _call_llm(_SYSTEM, prompt), "source": "ai"}
        except (urllib.error.URLError, urllib.error.HTTPError, KeyError, TimeoutError, ValueError):
            # Fall back to the offline engine on any LLM/network failure.
            pass

    return {"answer": _local_answer(question, stats, derived), "source": "local"}


def generate_report(class_id: int) -> dict:
    stats, derived = build_context(class_id)
    if stats["totals"]["enrolled"] == 0:
        return {"report": "No students enrolled yet — nothing to report.", "source": "local"}

    if config.ai_enabled():
        try:
            facts = _facts_payload(stats, derived)
            prompt = (
                "Attendance facts (JSON):\n" + facts + "\n\n"
                "Write a concise attendance report (120-180 words) for the class "
                "admin. Cover: overall participation today and across all recorded "
                "days, standout students, and anyone who needs attention (low "
                "attendance). Use short paragraphs or bullet points. Only use the "
                "facts provided."
            )
            return {"report": _call_llm(_SYSTEM, prompt), "source": "ai"}
        except (urllib.error.URLError, urllib.error.HTTPError, KeyError, TimeoutError, ValueError):
            pass

    return {"report": _local_report(stats, derived), "source": "local"}
