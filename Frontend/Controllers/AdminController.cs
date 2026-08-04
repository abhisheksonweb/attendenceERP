using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Infrastructure.Frm;
using MedicalCollege.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly IClassService _classes;
    private readonly IStudentService _students;
    private readonly IRequestService _requests;
    private readonly INotificationService _notifications;
    private readonly IAttendanceService _attendance;
    private readonly IParentNotificationService _parents;
    private readonly IFrmClient _frm;
    private readonly IWebHostEnvironment _env;

    public AdminController(
        IClassService classes,
        IStudentService students,
        IRequestService requests,
        INotificationService notifications,
        IAttendanceService attendance,
        IParentNotificationService parents,
        IFrmClient frm,
        IWebHostEnvironment env)
    {
        _classes = classes;
        _students = students;
        _requests = requests;
        _notifications = notifications;
        _attendance = attendance;
        _parents = parents;
        _frm = frm;
        _env = env;
    }

    /// <summary>Class-first admin dashboard.</summary>
    public async Task<IActionResult> Index()
    {
        var classes = await _classes.GetAllAsync();
        ViewBag.Notifications = await _notifications.GetForUserAsync(User.GetUserId());
        return View(classes);
    }

    [HttpGet]
    public IActionResult CreateClass() => View(new ClassFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateClass(ClassFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _classes.CreateAsync(model, User.GetUserId(), User.GetDisplayName());
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Unable to create class.");
            return View(model);
        }

        if (result.Data?.Id is not null)
            await SyncClassInternalAsync(result.Data.Id);

        TempData["Success"] = "Class created.";
        return RedirectToAction(nameof(ClassDetail), new { id = result.Data!.Id });
    }

    public async Task<IActionResult> ClassDetail(string id)
    {
        var detail = await _classes.GetDetailAsync(id);
        if (detail is null) return NotFound();

        if (detail.Class.Id is not null)
        {
            detail.FaceRecognizeUrl = await _frm.GetRecognizeUrlAsync(detail.Class.Id);
            if (detail.FaceRecognizeUrl is null && detail.Class.FrmClassId is null)
            {
                await SyncClassInternalAsync(id);
                detail = await _classes.GetDetailAsync(id) ?? detail;
                detail.FaceRecognizeUrl = await _frm.GetRecognizeUrlAsync(id);
            }
        }

        ViewBag.Notifications = (await _notifications.GetForUserAsync(User.GetUserId()))
            .Where(n => string.Equals(n.ClassId, id, StringComparison.OrdinalIgnoreCase))
            .Take(15)
            .ToList();
        return View(detail);
    }

    [HttpGet]
    public async Task<IActionResult> EditClass(string id)
    {
        var model = await _classes.GetByIdAsync(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditClass(ClassFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _classes.UpdateAsync(model, User.GetUserId(), User.GetDisplayName());
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Update failed.");
            return View(model);
        }
        if (model.Id is not null) await SyncClassInternalAsync(model.Id);
        TempData["Success"] = "Class updated.";
        return RedirectToAction(nameof(ClassDetail), new { id = model.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncFaceModule(string id)
    {
        var ok = await SyncClassInternalAsync(id);
        TempData[ok ? "Success" : "Error"] = ok
            ? "Class roster synced to Face Recognition module."
            : "Could not sync. Is FRModule running on http://127.0.0.1:8000?";
        return RedirectToAction(nameof(ClassDetail), new { id });
    }

    public async Task<IActionResult> FaceRecognition(string id)
    {
        var detail = await _classes.GetDetailAsync(id);
        if (detail is null) return NotFound();
        var url = await _frm.GetRecognizeUrlAsync(id);
        if (url is null)
        {
            await SyncClassInternalAsync(id);
            url = await _frm.GetRecognizeUrlAsync(id);
        }
        ViewBag.ClassName = detail.Class.Name;
        ViewBag.RecognizeUrl = url;
        ViewBag.ClassId = id;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> LiveAttendance(string id)
    {
        var detail = await _classes.GetDetailAsync(id);
        if (detail is null) return NotFound();

        var codes = detail.Students.Select(s => s.StudentId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var today = (await _attendance.GetDailyAsync(DateTime.Today))
            .Where(a => codes.Contains(a.StudentCode))
            .OrderBy(a => a.StudentName)
            .ToList();

        var occupancyInNow = 0;
        if (detail.Class.FrmClassId.HasValue)
        {
            var live = await _frm.GetClassAttendanceAsync(detail.Class.FrmClassId.Value);
            occupancyInNow = live?.Count(r => r.Status.Equals("IN", StringComparison.OrdinalIgnoreCase)) ?? 0;
        }

        return Json(new
        {
            syncedAt = DateTime.Now.ToString("HH:mm:ss"),
            present = today.Count(r => r.Status is "Present" or "Late"),
            earlyLeave = today.Count(r => r.EarlyLeave || r.Status == "PartialAbsent"),
            partialAbsent = today.Count(r => r.Status == "PartialAbsent" || r.EarlyLeave),
            total = detail.Students.Count(s => s.IsActive),
            occupancyInNow,
            rows = today.Select(r => new
            {
                r.StudentCode,
                r.StudentName,
                r.Status,
                r.Source,
                remarks = r.Remarks,
                firstIn = r.FirstIn,
                lastOut = r.LastOut,
                duration = r.Duration,
                earlyLeave = r.EarlyLeave,
                date = r.Date.ToString("yyyy-MM-dd")
            })
        });
    }

    /// <summary>Complete attendance history for a class — portal days + face IN/OUT visits.</summary>
    public async Task<IActionResult> AttendanceLog(string id)
    {
        var detail = await _classes.GetDetailAsync(id);
        if (detail is null) return NotFound();

        var allHistory = new List<AttendanceRecordViewModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in detail.Students)
        {
            if (string.IsNullOrEmpty(s.Id)) continue;
            foreach (var r in await _attendance.GetByStudentAsync(s.Id))
            {
                if (seen.Add(r.Id))
                    allHistory.Add(r);
            }
        }

        IReadOnlyList<FrmSessionRow> sessions = Array.Empty<FrmSessionRow>();
        if (detail.Class.FrmClassId.HasValue)
        {
            sessions = await _frm.GetClassSessionsAsync(detail.Class.FrmClassId.Value)
                       ?? Array.Empty<FrmSessionRow>();
            sessions = sessions
                .OrderByDescending(s => s.EntryTs ?? s.Date)
                .ThenBy(s => s.Name)
                .ToList();
        }

        ViewBag.Class = detail.Class;
        ViewBag.PortalRecords = allHistory
            .OrderByDescending(r => r.Date)
            .ThenBy(r => r.StudentName)
            .ToList();
        ViewBag.Sessions = sessions;
        return View();
    }

    public async Task<IActionResult> Students([FromQuery] StudentListFilter filter, string? classId = null)
    {
        filter ??= new StudentListFilter();
        filter.Page = filter.Page <= 0 ? 1 : filter.Page;
        if (filter.PageSize <= 0) filter.PageSize = 10;

        var result = await _students.SearchAsync(filter);
        if (!string.IsNullOrWhiteSpace(classId))
        {
            // Re-run with all matching students for this class (avoid filtering only the current page).
            filter.Page = 1;
            filter.PageSize = 500;
            result = await _students.SearchAsync(filter);
            var classItems = result.Items.Where(s => s.ClassId == classId).ToList();
            result = new PagedResult<StudentFormViewModel>
            {
                Items = classItems,
                Page = 1,
                PageSize = classItems.Count,
                TotalCount = classItems.Count
            };
        }
        ViewBag.Filter = filter;
        ViewBag.ClassId = classId;
        var classes = await _classes.GetAllAsync();
        ViewBag.Classes = classes;
        ViewBag.Departments = classes
            .Select(c => c.Department)
            .Concat(result.Items.Select(s => s.Department))
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d)
            .ToList();
        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> CreateStudent(string? classId = null)
    {
        var classes = await _classes.GetAllAsync();
        ViewBag.Classes = classes;

        if (string.IsNullOrWhiteSpace(classId))
        {
            if (!classes.Any(c => c.IsActive))
            {
                TempData["Error"] = "Create a class first, then add students to that batch.";
                return RedirectToAction(nameof(CreateClass));
            }

            TempData["Error"] = "Open a class and use Add Student, or pick a class from Quick Actions.";
            return RedirectToAction(nameof(Index));
        }

        var cls = classes.FirstOrDefault(c => c.Id == classId);
        if (cls is null)
        {
            TempData["Error"] = "Class not found.";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.ClassName = $"{cls.Name} ({cls.Code})";
        var nextId = await _students.GenerateNextStudentIdAsync(cls.Course, cls.Department);
        return View(new StudentFormViewModel
        {
            TemporaryPassword = null,
            ForcePasswordChange = true,
            ClassId = cls.Id,
            Course = cls.Course,
            Department = cls.Department,
            Semester = cls.Semester,
            StudentId = nextId,
            EnrollmentNumber = nextId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStudent(StudentFormViewModel model, IFormFile? profilePhoto)
    {
        var classes = await _classes.GetAllAsync();
        ViewBag.Classes = classes;
        var cls = classes.FirstOrDefault(c => c.Id == model.ClassId);
        ViewBag.ClassName = cls is null ? null : $"{cls.Name} ({cls.Code})";

        // Department / course / semester come from the class — not collected on this form.
        if (cls is not null)
        {
            model.Department = cls.Department;
            model.Course = cls.Course;
            model.Semester = cls.Semester;
        }

        ModelState.Remove(nameof(model.Username));
        ModelState.Remove(nameof(model.StudentId));
        ModelState.Remove(nameof(model.EnrollmentNumber));
        ModelState.Remove(nameof(model.TemporaryPassword));
        ModelState.Remove(nameof(model.Department));
        ModelState.Remove(nameof(model.Course));
        ModelState.Remove(nameof(model.Semester));

        if (!ModelState.IsValid) return View(model);

        // Face photo is for FRModule only — profile picture is uploaded later by the student.
        model.ProfilePhotoPath = null;

        var result = await _students.CreateStudentAsync(model, User.GetUserId(), User.GetDisplayName());
        if (!result.Success)
        {
            var err = result.Message ?? "Unable to create student.";
            ModelState.AddModelError(string.Empty, err);
            TempData["Error"] = err;
            if (cls is not null)
            {
                model.StudentId = await _students.GenerateNextStudentIdAsync(cls.Course, cls.Department);
                model.EnrollmentNumber = model.StudentId;
            }
            return View(model);
        }

        if (!string.IsNullOrWhiteSpace(model.ClassId))
            await SyncClassInternalAsync(model.ClassId);

        var created = result.Data;
        var successMsg = result.Message ?? "Student added.";
        if (created?.Id is not null && profilePhoto is { Length: > 0 })
        {
            var (ok, detail) = await TryEnrollStudentFaceAsync(
                created.Id, model.ClassId!, profilePhoto, null);
            successMsg += ok
                ? " Face enrolled for attendance."
                : $" Face not enrolled ({detail}).";
            if (!ok) TempData["Error"] = $"Student saved, but face enrollment failed: {detail}";
        }

        TempData["Success"] = successMsg;
        return RedirectToAction(nameof(ClassDetail), new { id = model.ClassId });
    }

    [HttpGet]
    public async Task<IActionResult> EditStudent(string id)
    {
        var student = await _students.GetByIdAsync(id);
        if (student is null) return NotFound();
        var classes = await _classes.GetAllAsync();
        ViewBag.Classes = classes;
        return View(student);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditStudent(StudentFormViewModel model, IFormFile? profilePhoto)
    {
        var classes = await _classes.GetAllAsync();
        ViewBag.Classes = classes;
        var cls = classes.FirstOrDefault(c => c.Id == model.ClassId);
        if (cls is not null)
        {
            model.Department = cls.Department;
            model.Course = cls.Course;
            model.Semester = cls.Semester;
        }
        ModelState.Remove(nameof(model.Username));
        ModelState.Remove(nameof(model.Department));
        ModelState.Remove(nameof(model.Course));
        ModelState.Remove(nameof(model.Semester));
        if (!ModelState.IsValid) return View(model);

        // Keep existing profile picture; admin face file only updates FRModule enrollment.
        IFormFile? facePhoto = profilePhoto is { Length: > 0 } ? profilePhoto : null;

        var result = await _students.UpdateStudentAsync(model, User.GetUserId(), User.GetDisplayName());
        if (!result.Success)
        {
            var err = result.Message ?? "Update failed.";
            ModelState.AddModelError(string.Empty, err);
            TempData["Error"] = err;
            return View(model);
        }
        if (!string.IsNullOrWhiteSpace(model.ClassId))
            await SyncClassInternalAsync(model.ClassId);

        var faceMsg = "";
        if (facePhoto is not null && !string.IsNullOrWhiteSpace(model.Id) && !string.IsNullOrWhiteSpace(model.ClassId))
        {
            var (ok, detail) = await TryEnrollStudentFaceAsync(model.Id!, model.ClassId!, facePhoto, null);
            faceMsg = ok
                ? " Face enrolled for attendance."
                : $" Face not enrolled ({detail}).";
            if (!ok) TempData["Error"] = $"Student updated, but face enrollment failed: {detail}";
        }

        TempData["Success"] = "Student updated." + faceMsg;
        return RedirectToAction(nameof(ClassDetail), new { id = model.ClassId });
    }

    public async Task<IActionResult> StudentDetails(string id)
    {
        var student = await _students.GetByIdAsync(id);
        if (student is null) return NotFound();
        ViewBag.Attendance = await _attendance.GetByStudentAsync(id);
        return View(student);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateStudent(string id, string? classId = null)
    {
        var result = await _students.DeactivateStudentAsync(id, User.GetUserId(), User.GetDisplayName());
        TempData[result.Success ? "Success" : "Error"] = result.Message ?? (result.Success ? "Student deactivated." : "Failed.");
        return string.IsNullOrWhiteSpace(classId)
            ? RedirectToAction(nameof(Students))
            : RedirectToAction(nameof(ClassDetail), new { id = classId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStudent(string id, string? classId = null, string? returnTo = null)
    {
        var student = await _students.GetByIdAsync(id);
        if (student is null)
        {
            TempData["Error"] = "Student not found.";
            return RedirectToAction(nameof(Students));
        }

        var targetClassId = classId ?? student.ClassId;
        var frmStudentId = student.FrmStudentId;

        var result = await _students.DeleteStudentAsync(id, User.GetUserId(), User.GetDisplayName());
        if (result.Success && frmStudentId.HasValue)
            await _frm.DeleteStudentAsync(frmStudentId.Value);

        if (result.Success && !string.IsNullOrWhiteSpace(targetClassId))
            await SyncClassInternalAsync(targetClassId);

        TempData[result.Success ? "Success" : "Error"] = result.Message ?? (result.Success ? "Student deleted." : "Failed.");

        if (string.Equals(returnTo, "details", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(targetClassId))
            return RedirectToAction(nameof(Students));

        return RedirectToAction(nameof(ClassDetail), new { id = targetClassId });
    }

    [HttpGet]
    public IActionResult ResetStudentPassword(string id)
        => View(new ResetPasswordViewModel { UserId = id });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetStudentPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _students.ResetStudentPasswordAsync(model.UserId, model.NewPassword, User.GetUserId(), User.GetDisplayName());
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Reset failed.");
            return View(model);
        }
        TempData["Success"] = "Student password reset.";
        return RedirectToAction(nameof(Students));
    }

    [HttpGet]
    public async Task<IActionResult> ImportStudents()
    {
        ViewBag.Classes = await _classes.GetAllAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> ImportStudents(IFormFile? file, string? defaultClassId)
    {
        var classes = await _classes.GetAllAsync();
        ViewBag.Classes = classes;
        ViewBag.SelectedClassId = defaultClassId;

        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose a CSV file to upload.");
            return View();
        }

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "Only .csv files are supported. Save Excel as CSV first.");
            return View();
        }

        await using var stream = file.OpenReadStream();
        var result = await _students.ImportFromCsvAsync(
            stream, defaultClassId, User.GetUserId(), User.GetDisplayName());

        if (!result.Success || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Import failed.");
            return View();
        }

        foreach (var classId in result.Data.AffectedClassIds)
            await SyncClassInternalAsync(classId);

        var faceOk = 0;
        var faceFail = 0;
        foreach (var row in result.Data.Rows.Where(r =>
                     r.Success &&
                     !string.IsNullOrWhiteSpace(r.PhotoUrl) &&
                     !string.IsNullOrWhiteSpace(r.PortalStudentId) &&
                     !string.IsNullOrWhiteSpace(r.ClassId)))
        {
            var enrolled = await EnrollFaceFromPhotoUrlAsync(row);
            if (enrolled)
            {
                faceOk++;
                row.FaceEnrolled = true;
                row.Message += " Face enrolled from photo link.";
            }
            else
            {
                faceFail++;
                row.Message += " Photo saved, but face auto-enroll failed (check FRModule / photo URL).";
            }
        }

        var msg = result.Message ?? "Import finished.";
        if (faceOk + faceFail > 0)
            msg += $" Face enroll: {faceOk} ok, {faceFail} failed.";
        TempData["Success"] = msg;
        ViewBag.ImportResult = result.Data;
        return View();
    }

    [HttpGet]
    public IActionResult ImportStudentsTemplate()
    {
        const string csv =
            "ClassCode,Name,Email,Mobile,StudentId,EnrollmentNumber,DateOfBirth,Gender,GuardianName,GuardianPhone,GuardianEmail,PhotoUrl\r\n" +
            "MBBS-Y1-A,Riya Sharma,riya.sharma@example.com,9876543210,,,2005-04-12,Female,Parent Sharma,9876500001,parent.riya@example.com,https://example.com/photos/riya.jpg\r\n" +
            ",Amit Patel,amit.patel@example.com,9876543211,,,2004-11-03,Male,,,,\r\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv", "student-import-template.csv");
    }

    public async Task<IActionResult> Requests()
    {
        ViewBag.Pending = await _requests.GetPendingAsync();
        ViewBag.History = await _requests.GetHistoryAsync();
        return View();
    }

    public async Task<IActionResult> ParentAlerts(string? classId = null)
    {
        var alerts = await _parents.GetRecentAsync(200);
        if (!string.IsNullOrWhiteSpace(classId))
            alerts = alerts.Where(a => string.Equals(a.ClassId, classId, StringComparison.OrdinalIgnoreCase)).ToList();

        ViewBag.Alerts = alerts;
        ViewBag.Classes = await _classes.GetAllAsync();
        ViewBag.SelectedClassId = classId;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveRequest(string id, string? remarks)
    {
        var result = await _requests.ApproveAsync(id, remarks, User.GetUserId(), User.GetDisplayName());
        TempData[result.Success ? "Success" : "Error"] = result.Message ?? (result.Success ? "Request approved." : "Failed.");
        return RedirectToAction(nameof(Requests));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectRequest(string id, string? remarks)
    {
        var result = await _requests.RejectAsync(id, remarks, User.GetUserId(), User.GetDisplayName());
        TempData[result.Success ? "Success" : "Error"] = result.Message ?? (result.Success ? "Request rejected." : "Failed.");
        return RedirectToAction(nameof(Requests));
    }

    public async Task<IActionResult> Notifications(string? classId = null)
    {
        var items = await _notifications.GetForUserAsync(User.GetUserId());
        if (!string.IsNullOrWhiteSpace(classId))
            items = items.Where(n => string.Equals(n.ClassId, classId, StringComparison.OrdinalIgnoreCase)).ToList();

        ViewBag.Classes = await _classes.GetAllAsync();
        ViewBag.SelectedClassId = classId;
        return View(items);
    }

    private async Task<(bool Ok, string Message)> TryEnrollStudentFaceAsync(
        string portalStudentId,
        string classId,
        IFormFile? uploadedPhoto,
        string? photoPath)
    {
        if (string.IsNullOrWhiteSpace(classId))
            return (false, "No class assigned.");

        await SyncClassInternalAsync(classId);
        var detail = await _classes.GetDetailAsync(classId);
        var frmClassId = detail?.Class.FrmClassId;
        if (frmClassId is null)
            return (false, "Could not sync class to face module. Is FRModule running?");

        var student = detail?.Students.FirstOrDefault(s => s.Id == portalStudentId)
                      ?? await _students.GetByIdAsync(portalStudentId);
        if (student is null)
            return (false, "Student not found.");

        var meta = new FrmEnrollFromFileRequest
        {
            Name = student.Name,
            RollNo = student.StudentId,
            Email = student.Email,
            Phone = student.Mobile,
            ExternalId = student.Id!
        };

        FrmEnrollResult? enroll = null;

        if (uploadedPhoto is { Length: > 0 })
        {
            await using var stream = uploadedPhoto.OpenReadStream();
            enroll = await _frm.EnrollFromPhotoFileAsync(
                frmClassId.Value, meta, stream, uploadedPhoto.FileName, uploadedPhoto.ContentType);
        }
        else if (!string.IsNullOrWhiteSpace(photoPath) &&
                 photoPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            enroll = await _frm.EnrollFromPhotoUrlAsync(frmClassId.Value, new FrmEnrollFromUrlRequest
            {
                PhotoUrl = photoPath,
                Name = meta.Name,
                RollNo = meta.RollNo,
                Email = meta.Email,
                Phone = meta.Phone,
                ExternalId = meta.ExternalId
            });
        }
        else if (!string.IsNullOrWhiteSpace(photoPath) && photoPath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            var localPath = Path.Combine(_env.WebRootPath, photoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(localPath))
                return (false, "Saved photo file not found on disk.");

            await using var fs = System.IO.File.OpenRead(localPath);
            enroll = await _frm.EnrollFromPhotoFileAsync(
                frmClassId.Value, meta, fs, Path.GetFileName(localPath), "image/jpeg");
        }
        else
        {
            return (false, "No face photo provided.");
        }

        if (enroll is null || !enroll.Ok)
            return (false, enroll?.Error ?? enroll?.Message ?? "Face module rejected the photo.");

        await _students.MarkFaceEnrolledAsync(student.Id!, enroll.FrmStudentId);
        return (true, enroll.Message ?? "Face enrolled.");
    }

    private async Task<bool> EnrollFaceFromPhotoUrlAsync(StudentImportRowResult row)
    {
        if (string.IsNullOrWhiteSpace(row.ClassId) ||
            string.IsNullOrWhiteSpace(row.PortalStudentId) ||
            string.IsNullOrWhiteSpace(row.PhotoUrl))
            return false;

        var detail = await _classes.GetDetailAsync(row.ClassId);
        var frmClassId = detail?.Class.FrmClassId;
        if (frmClassId is null)
        {
            await SyncClassInternalAsync(row.ClassId);
            detail = await _classes.GetDetailAsync(row.ClassId);
            frmClassId = detail?.Class.FrmClassId;
        }

        if (frmClassId is null) return false;

        var student = detail?.Students.FirstOrDefault(s => s.Id == row.PortalStudentId)
                      ?? await _students.GetByIdAsync(row.PortalStudentId);
        if (student is null) return false;

        var enroll = await _frm.EnrollFromPhotoUrlAsync(frmClassId.Value, new FrmEnrollFromUrlRequest
        {
            PhotoUrl = row.PhotoUrl!,
            Name = student.Name,
            RollNo = student.StudentId,
            Email = student.Email,
            Phone = student.Mobile,
            ExternalId = student.Id!
        });

        if (enroll is null || !enroll.Ok)
            return false;

        await _students.MarkFaceEnrolledAsync(student.Id!, enroll.FrmStudentId);
        return true;
    }

    private async Task<bool> SyncClassInternalAsync(string classId)
    {
        var detail = await _classes.GetDetailAsync(classId);
        if (detail?.Class.Id is null) return false;

        var request = new FrmSyncRequest
        {
            ExternalId = detail.Class.Id,
            Name = detail.Class.Name,
            Code = detail.Class.Code,
            Students = detail.Students.Where(s => s.IsActive).Select(s => new FrmSyncStudent
            {
                ExternalId = s.Id!,
                Name = s.Name,
                RollNo = s.StudentId,
                Email = s.Email,
                Phone = s.Mobile
            }).ToList()
        };

        var result = await _frm.SyncClassAsync(request);
        if (result is null || !result.Ok) return false;

        await _classes.SetFrmClassIdAsync(classId, result.FrmClassId);
        return true;
    }

    private async Task<string> SavePhotoAsync(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
        var name = $"{Guid.NewGuid():N}{ext}";
        var folder = Path.Combine(_env.WebRootPath, "uploads", "profiles");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        await using var stream = System.IO.File.Create(path);
        await file.CopyToAsync(stream);
        return $"/uploads/profiles/{name}";
    }
}
