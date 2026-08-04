# MedCollege Campus ERP — Client Demo Guide

**Product:** MedCollege Campus ERP (AI-Powered Attendance)  
**Audience:** College / institute stakeholders  
**Demo length:** ~20–30 minutes  
**Deployment model:** On-premises (face data & attendance stay on campus)

Use this document as a **speaking script**. Sections marked **Say** are what you tell the client. Sections marked **Do** are the clicks/actions on screen.

---

## 1. One-minute elevator pitch

**Say:**

> MedCollege is an on-premises Campus ERP focused on **AI face-recognition attendance**.  
> Students walk into class, look at the camera, and the system marks them **Present (IN)**. When they leave, it marks **OUT**.  
> At **6:00 PM**, parents of students who were **absent that day** get an email alert.  
> Admins manage classes, students, attendance, reports, and parent alerts from one portal.  
> Face photos and attendance logs stay on the college server — designed for **NMC / data-privacy** expectations.

---

## 2. What the solution includes

| Module | What it does |
|--------|----------------|
| **Admin Portal** | Classes, students, face attendance, logs, reports, parent alerts, notifications |
| **Student Portal** | Dashboard, profile, attendance history, face self-enrollment, notifications |
| **Face Recognition Module (FRModule)** | Live camera, face enroll, IN/OUT recognition |
| **Parent Alerts** | Daily absence email at configured time (default **6:00 PM**) |
| **Reports** | Daily / monthly attendance, occupancy, department & course views |

---

## 3. Architecture (keep this short for clients)

**Say:**

> There are two apps working together:

1. **Campus Portal** — ASP.NET Core web app (admin + student UI)  
2. **Face Module** — local AI service (Flask + OpenCV) connected to the webcam  

```
[Webcam] → [Face Module :8000] → [Campus Portal :5148] → [Parent Email via SMTP]
                                      ↓
                               Local data store (on-prem)
```

**Key point:** Face embeddings and attendance stay **on campus**. Email is only used to notify guardians.

---

## 4. Demo setup (before the meeting)

### Start services (in order)

**1. Face module**

```bash
cd FRModule
.venv\Scripts\Activate.ps1
python app.py
```

→ Confirm: `http://127.0.0.1:8000`

**2. Campus portal**

```bash
dotnet run --project Frontend/MedicalCollege.Web.csproj
```

→ Open: `http://localhost:5148`

### Demo logins

| Role | Email | Password |
|------|-------|----------|
| **Admin** | `admin@medcollege.edu` | `Admin@123` |
| **Student** | *(created during demo)* | Default on create: `Temp@123` (forced change on first login) |

### Checklist before you start

- [ ] Webcam connected and working  
- [ ] Face module running (port **8000**)  
- [ ] Portal running (port **5148**)  
- [ ] At least one class with 1–2 students who have face photos enrolled  
- [ ] One student **without** attendance today (to show parent absence alert)  
- [ ] Guardian email filled for that student (if showing live email)  
- [ ] Parent alert time set in config (see §8) — for daytime demos, temporarily set a near-future time

---

## 5. Live demo script (recommended order)

### Step A — Login & portal overview (2 min)

**Do:** Open `http://localhost:5148` → Login as admin.

**Say:**

> This is the admin portal. From the sidebar we manage the full attendance lifecycle:  
> **Classes → Students → Face Attendance → Logs → Parent Alerts → Reports.**

Point to sidebar: Classes, All Students, Attendance, Parent Alerts, Reports, Notifications.

---

### Step B — Class (batch) management (3 min)

**Do:** Administration → **Classes** → **Create Class** (or open an existing class).

Fill / show:

- Class name & code (e.g. `MBBS-Y1-A`)  
- Department / Course / Semester  
- Class start & end times (end time is used for early-leave detection for admins)

**Say:**

> Each class is a batch. We sync the roster to the face module so recognition knows which students belong to this class.

**Do:** Open the class → click **Sync Face Module** (if available) / confirm class is linked.

---

### Step C — Add student + face enrollment (4 min)

**Do:** Class → **Add Student** (or **All Students** → create).

Show:

- Student details (name, email, mobile, student ID)  
- Guardian name / phone / **email** (for parent alerts)  
- **Face photo upload** (required on create — this enrolls the face for attendance)

**Say:**

> When admin adds a student with a face photo, the system enrolls that face into the AI module automatically.  
> The student’s own profile picture in the student portal is separate — it does not replace the attendance face.

**Optional — bulk import:**

**Do:** All Students → Import → download template.

**Say:**

> For large batches we support CSV/Excel import, including optional photo URLs for automatic face enrollment.

---

### Step D — Live face attendance IN / OUT (6–8 min) — *hero moment*

**Do:** Open the class → **Face Recognition** (keep this page open).

**Say:**

> Attendance is captured when Face Recognition is running for the class.  
> Student looks at the camera → system recognizes the face → marks **IN** (Present).  
> When they leave and are recognized again → marks **OUT**.  
> There is a short debounce so one person is not toggled repeatedly in a few seconds.

**Do:**

1. Ask a demo student to face the camera → show **IN**  
2. Wait briefly / show portal “Today” attendance updating  
3. Ask them to face again → show **OUT**  
4. Open **Attendance Log** for the class → show First In / Last Out  

**Say:**

> The portal syncs with the face module every few seconds, so admin sees live presence without manual roll call.

**Optional talking point — early leave:**

> If a student marks OUT before class end time, the **admin** gets a notification. Parent email is reserved for **daily absence**, not every IN/OUT.

---

### Step E — Student portal (3 min)

**Do:** Logout → login as a student (use a demo student account).

Show:

- Dashboard  
- Attendance History (IN/OUT times)  
- Profile / Change Password (first login may force password change)  
- Face Enrollment (self-service option)  
- Notifications  

**Say:**

> Students can check their own attendance history and keep their profile updated.  
> Sensitive corrections can go through a request workflow for admin approval.

---

### Step F — Parent absence alert at 6 PM (4 min)

**Say:**

> Parents do **not** get an email for every check-in. That would be noisy.  
> Instead, once a day at **6:00 PM**, the scheduler reviews today’s attendance.  
> Any active student who was **not Present or Late** that day triggers an **absence alert** to the guardian email.  
> Alerts are also logged on-premises under **Parent Alerts** for audit / NMC compliance.  
> Guardians can unsubscribe via a link in the email.

**Do:**

1. Ensure one student has **no Present/Late** record for today  
2. Open **Parent Alerts** after the scheduled time (or after you temporarily set the time for demo)  
3. Show the absence alert row + delivery note (email sent / logged)

**Demo tip:** If the meeting is not at 6 PM, temporarily set:

```json
"ParentAlerts": {
  "DailyMissedClassTime": "18:00"
}
```

…to a time 2–3 minutes ahead, restart the portal, wait for the run, then set it back to `18:00`.

---

### Step G — Reports & notifications (2 min)

**Do:** Open **Reports** and **Attendance** views (daily / monthly / statistics). Open **Notifications**.

**Say:**

> Admin can monitor daily and monthly attendance, see who is currently IN, and review system notifications such as early leave or wrong-class detections.

---

## 6. Feature summary (handout / slide bullets)

1. **AI face attendance** — contactless IN/OUT with live camera  
2. **Class & student management** — create, edit, CSV import  
3. **Auto face enroll** — from admin photo upload or import photo URL  
4. **Student self-service** — profile, attendance history, face enrollment  
5. **Parent absence alerts** — once daily (6 PM), absence only  
6. **Admin early-leave alerts** — OUT before class end  
7. **On-premises data** — face + attendance stored locally  
8. **Audit trail** — parent alert log + notifications  
9. **Optional ERP push** — can integrate with college ERP when enabled  
10. **Reports** — attendance, occupancy, department/course views  

---

## 7. Roles at a glance

| Role | Access |
|------|--------|
| **Admin** | Full administration: classes, students, face attendance, alerts, reports |
| **Student** | Own dashboard, profile, attendance, face enrollment, notifications |
| **Parent** | No login — receives absence email + unsubscribe link |

---

## 8. Configuration the client should know

| Setting | Purpose | Typical value |
|---------|---------|----------------|
| `Frm:BaseUrl` | Face module URL | `http://127.0.0.1:8000` |
| `ParentAlerts:DailyMissedClassTime` | Daily absence scheduler | `18:00` |
| `ParentAlerts:EnableEmail` | Send live emails | `true` / `false` |
| `Smtp:*` | College SMTP for parent mail | College mail server |
| `Hosting:Mode` | Deployment posture | `OnPremises` |
| `Erp:Enabled` | Push attendance to ERP | `false` until integrated |

> **Note for presenters:** Do not display SMTP passwords or API keys on screen during the demo.

---

## 9. Suggested Q&A answers

**Q: Does it work without internet?**  
**A:** Yes for attendance and portal. Internet is needed only if you want live parent email via SMTP / optional cloud ERP.

**Q: Where is face data stored?**  
**A:** On the college server in the Face Module (local database + embeddings). Not uploaded to a public cloud by default.

**Q: Will parents get messages all day?**  
**A:** No. Parent email is **absence-only**, once per day at the scheduled time (default 6 PM).

**Q: What if the wrong student walks into another class?**  
**A:** The system can detect wrong-class recognition and notify admin.

**Q: Can we import an entire batch?**  
**A:** Yes — CSV/Excel import with optional photo links for face enrollment.

**Q: Can this connect to our existing college ERP?**  
**A:** Yes — there is an optional ERP integration switch; we enable and map it during rollout.

**Q: Is biometric data NMC-friendly?**  
**A:** Designed as **on-premises primary** so biometric and attendance records remain under institute control.

---

## 10. Closing slide / closing words

**Say:**

> To summarize: MedCollege replaces manual attendance with **AI face recognition**, gives admins a full campus attendance console, gives students self-service visibility, and keeps parents informed **only when it matters** — with a single daily absence alert.  
> Everything critical stays **on campus**.  
> Next step: we can run a pilot on one class/batch for 1–2 weeks and review attendance accuracy + parent alert delivery together.

---

## 11. Quick runbook (presenter cheat sheet)

| Time | Action |
|------|--------|
| T−10 min | Start FRModule + Portal; test camera |
| 0:00 | Elevator pitch |
| 0:02 | Admin login + menu tour |
| 0:05 | Class + student + face photo |
| 0:10 | Live Face Recognition IN/OUT |
| 0:18 | Student portal |
| 0:22 | Parent Alerts story (+ live email if timed) |
| 0:26 | Reports + Q&A |
| 0:30 | Close + propose pilot |

---

## 12. Project layout (technical, if asked)

```
AIpoweredattendence/
├── Frontend/          # Campus portal (ASP.NET Core MVC)
├── Backend/           # Domain / Application / Infrastructure / API
├── FRModule/          # Face recognition (Flask + camera AI)
└── CLIENT_DEMO_GUIDE.md
```

---

*Document for client demos of the MedCollege Campus ERP AI Attendance solution.*
