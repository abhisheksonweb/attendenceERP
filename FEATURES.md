# MedCollege Campus ERP — Complete Features

**Product:** MedCollege Campus ERP (AI-Powered Attendance)  
**Model:** On-premises campus attendance + admin/student portals + guardian absence alerts

---

## 1. Product overview

MedCollege marks student attendance with **AI face recognition** (IN/OUT), gives admins a full campus console (classes, students, logs, reports, alerts), and notifies parents **once per day** for absence only (default **6:00 PM**). Face data and attendance stay on the college server.

---

## 2. User roles

| Role | What they can do |
|------|------------------|
| **Admin** | Classes, students, face attendance, logs, requests, parent alerts, reports, notifications |
| **Student** | Dashboard, profile, attendance history, activity history, notifications, face enrollment |
| **Parent / Guardian** | No login — receives absence email + unsubscribe link |
| **SuperAdmin** | Not available (login blocked) |

**Demo admin:** `admin@medcollege.edu` / `Admin@123`  
**New student temp password:** `Temp@123` (must change on first login)

---

## 3. Admin features

### Classes
- Create / edit class (name, code, department, course, semester, start & end times)
- Class roster and present-today counts
- **Sync Face Module** — push roster to AI module
- **Face Recognition** — live camera IN/OUT for that class
- **Attendance Log** — First In / Last Out / early leave

### Students
- Live search (name, ID, email, mobile) — filters as you type
- Filters: department, course, semester, active/inactive
- Column sorting + pagination
- Add student (face photo required → auto face enroll)
- Auto student ID generation
- Edit / details / delete / reset password
- Guardian name, phone, email
- Optional welcome / reset email via SMTP

### CSV import
- Import students from CSV (template download)
- Columns include ClassCode, student details, guardian contacts, optional PhotoUrl
- Auto-create class from ClassCode when needed
- PhotoUrl can auto-enroll face after import

### Attendance
- Daily / monthly / calendar / statistics views
- Present, Absent, and attendance %

### Face recognition (per class)
- Live camera recognition (IN ↔ OUT, debounce ~15 seconds)
- Portal sync every few seconds
- Wrong-class detection → admin notification
- Early leave (OUT before class end) → admin notification

### Requests
- Approve / reject student profile change requests
- Approve attendance correction (Absent → Present)
- Approve timing regularization (In/Out times)

### Parent Alerts
- On-premises audit log of guardian alerts
- Filter by class
- Shows delivery status (email sent / logged / unsubscribed)

### Reports
- Attendance, students, departments, courses
- Charts for department/course distribution  
- *Note: PDF/Excel export buttons are UI prototypes (not fully wired)*

### Notifications
- Admin inbox (early leave, wrong-class, system notices)
- Filter by class

---

## 4. Student portal features

- **Dashboard** — today status, %, present/absent days, recent attendance (live refresh)
- **Profile** — editable contact/address/guardian/photo; protected fields go through admin approval
- **Attendance History** — full day-by-day record; request Present or timing fix
- **Activity History** — personal activity log
- **Notifications** — view and mark read
- **Face Enrollment** — self-service face register (separate from profile photo)

---

## 5. Face recognition module (FRModule)

- Local AI: face detect + match (YuNet + SFace)
- Webcam / optional RTSP camera
- Per-class enroll and recognize
- IN/OUT toggle with debounce
- Wrong-class handling
- Local storage (SQLite + embeddings) — on-premises
- Default port: `http://127.0.0.1:8000`
- Attendance marking while admin Face Recognition page is open (unless headless mode is enabled)

---

## 6. Parent / guardian alerts

- **Absence only** (not every check-in/out)
- Runs once daily at configured time (`ParentAlerts:DailyMissedClassTime`, default **18:00**)
- Emails guardian if student was not Present/Late that day
- **Sundays skipped** (week off — no absence mail)
- Logged on-premises for audit / NMC-style compliance
- Unsubscribe link in email
- SMS flag exists but is **not** used (email/log only)

---

## 7. Attendance rules

| Rule | Behavior |
|------|----------|
| **IN / OUT** | First recognition = IN (Present); later = OUT; First In & Last Out stored |
| **Early leave** | OUT before class end → flag + admin alert (not parent email) |
| **Missing weekdays** | Auto-filled as **Absent** in student history/stats |
| **Sunday** | Shown as **Week Off** (not counted in Absent / %) |
| **Attendance %** | (Present + Late) ÷ (Present + Late + Absent) × 100 |
| **Parent alert** | Absence only, once/day at 6 PM (skips Sunday) |

---

## 8. Auth & security

- Cookie-based login (Remember Me supported)
- Change password (forced when required)
- Logout from profile menu
- Parent unsubscribe via secure token link
- Face Module API key protection
- Attendance face photo ≠ student profile picture

---

## 9. Technical stack & deployment

| Layer | Detail |
|-------|--------|
| **Portal** | ASP.NET Core 8 MVC — `http://localhost:5148` |
| **Face module** | Python Flask + OpenCV — `http://127.0.0.1:8000` |
| **Portal data** | JSON files under `Frontend/App_Data/` |
| **Face data** | Local SQLite + embeddings in `FRModule/data/` |
| **Hosting** | On-premises primary |
| **Email** | SMTP (e.g. Gmail) for parent + student mails |
| **ERP** | Optional HTTP push (`Erp:Enabled`, default off) |

---

## 10. Current limitations

- No parent login portal
- SuperAdmin login disabled
- Forgot Password is prototype UI only
- No parent SMS
- CSV import only (not native Excel)
- Report PDF/Excel export not fully wired
- Some AI module menu stubs are “Coming Soon”
- Portal uses JSON store (not production SQL yet)
- Live recognition normally needs Face Recognition page open

---

## 11. Quick feature checklist (client slide)

1. AI face attendance (IN/OUT)  
2. Class & student management  
3. CSV bulk import + photo enroll  
4. Student self-service portal  
5. Attendance history with Absent / Week Off calendar  
6. Timing & absence correction requests  
7. Daily parent absence email (6 PM)  
8. Admin early-leave & wrong-class alerts  
9. Reports & notifications  
10. On-premises face & attendance data  

---

*Document reflects the current MedCollege Campus ERP / AI attendance system.*
