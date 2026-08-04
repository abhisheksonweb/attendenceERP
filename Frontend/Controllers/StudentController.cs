using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Infrastructure.Frm;
using MedicalCollege.Web.Filters;
using MedicalCollege.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Web.Controllers;

[Authorize(Roles = "Student")]
[ServiceFilter(typeof(RequireStudentProfileFilter))]
public class StudentController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly IStudentService _students;
    private readonly IClassService _classes;
    private readonly IAttendanceService _attendance;
    private readonly INotificationService _notifications;
    private readonly IRequestService _requests;
    private readonly IFrmClient _frm;
    private readonly IActivityService _activity;
    private readonly IWebHostEnvironment _env;

    public StudentController(
        IDashboardService dashboard,
        IStudentService students,
        IClassService classes,
        IAttendanceService attendance,
        INotificationService notifications,
        IRequestService requests,
        IFrmClient frm,
        IActivityService activity,
        IWebHostEnvironment env)
    {
        _dashboard = dashboard;
        _students = students;
        _classes = classes;
        _attendance = attendance;
        _notifications = notifications;
        _requests = requests;
        _frm = frm;
        _activity = activity;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        var model = await _dashboard.GetStudentDashboardAsync(User.GetUserId());
        ViewBag.Notifications = await _notifications.GetForUserAsync(User.GetUserId());
        return View(model);
    }

    public async Task<IActionResult> Profile()
    {
        var profile = await _students.GetProfileAsync(User.GetUserId());
        if (profile is null) return NotFound();
        ViewBag.ProtectedFields = _students.GetProtectedFieldNames();
        return View(profile);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(StudentProfileViewModel model, IFormFile? profilePhoto)
    {
        if (profilePhoto is { Length: > 0 })
        {
            var ext = Path.GetExtension(profilePhoto.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            var name = $"{Guid.NewGuid():N}{ext}";
            var folder = Path.Combine(_env.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            await using (var stream = System.IO.File.Create(path))
                await profilePhoto.CopyToAsync(stream);
            model.ProfilePhotoPath = $"/uploads/profiles/{name}";
        }

        var result = await _students.UpdateEditableProfileAsync(User.GetUserId(), model);
        TempData[result.Success ? "Success" : "Error"] = result.Message ?? (result.Success ? "Profile saved." : "Save failed.");
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestUpdate(ProtectedFieldRequestViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please provide a valid update request.";
            return RedirectToAction(nameof(Profile));
        }

        var result = await _requests.CreateRequestAsync(User.GetUserId(), model);
        TempData[result.Success ? "Success" : "Error"] = result.Message ?? (result.Success ? "Update request submitted." : "Request failed.");
        return RedirectToAction(nameof(Profile));
    }

    public async Task<IActionResult> AttendanceHistory()
    {
        var profile = await _students.GetProfileAsync(User.GetUserId());
        if (profile is null) return NotFound();
        var records = await _attendance.GetByStudentAsync(profile.Id);
        var stats = await _attendance.GetStatsAsync(profile.Id);
        var myRequests = await _requests.GetByStudentAsync(profile.Id);
        var pendingDates = myRequests
            .Where(r => (r.FieldName.Equals("Attendance", StringComparison.OrdinalIgnoreCase)
                         || r.FieldName.Equals("AttendanceTiming", StringComparison.OrdinalIgnoreCase))
                        && r.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.OldValue)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ViewBag.Stats = stats;
        ViewBag.PendingAttendanceDates = pendingDates;
        return View(records);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReportAttendance(AttendanceCorrectionRequestViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please enter a valid reason for the attendance request.";
            return RedirectToAction(nameof(AttendanceHistory));
        }

        var result = await _requests.CreateAttendanceCorrectionAsync(User.GetUserId(), model);
        TempData[result.Success ? "Success" : "Error"] = result.Message
            ?? (result.Success ? "Request submitted." : "Unable to submit request.");
        return RedirectToAction(nameof(AttendanceHistory));
    }

    public async Task<IActionResult> Notifications()
    {
        var items = await _notifications.GetForUserAsync(User.GetUserId());
        return View(items);
    }

    public async Task<IActionResult> ActivityHistory()
    {
        var items = await _activity.GetForUserAsync(User.GetUserId(), 100);
        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotificationRead(string id)
    {
        await _notifications.MarkReadAsync(id);
        return RedirectToAction(nameof(Notifications));
    }

    [HttpGet]
    public async Task<IActionResult> LiveToday()
    {
        var student = await _students.GetByUserIdAsync(User.GetUserId());
        if (student?.Id is null) return Json(new { ok = false });

        var records = await _attendance.GetByStudentAsync(student.Id);
        var today = records.FirstOrDefault(r => r.Date.Date == DateTime.Today);
        var stats = await _attendance.GetStatsAsync(student.Id);
        var todayPresent = today is not null &&
            (today.Status.Equals("Present", StringComparison.OrdinalIgnoreCase)
             || today.Status.Equals("Late", StringComparison.OrdinalIgnoreCase));
        var todayPartial = today is not null &&
            today.Status.Equals("PartialAbsent", StringComparison.OrdinalIgnoreCase);
        return Json(new
        {
            ok = true,
            syncedAt = DateTime.Now.ToString("HH:mm:ss"),
            todayPresent,
            todayPartial,
            todayStatus = today?.Status ?? "",
            todayStatusLabel = AttendanceDisplay.StatusLabel(today?.Status),
            todaySource = today?.Source ?? "",
            todayRemarks = today?.Remarks ?? "",
            attendancePercentage = stats.Percentage,
            presentDays = stats.PresentCount,
            absentDays = stats.AbsentCount,
            recent = records.Take(8).Select(r => new
            {
                date = r.Date.ToString("yyyy-MM-dd"),
                status = r.Status,
                statusLabel = AttendanceDisplay.StatusLabel(r.Status),
                source = r.Source,
                remarks = r.Remarks
            })
        });
    }

    /// <summary>Student face enrollment using FRModule (capture + register).</summary>
    public async Task<IActionResult> FaceEnrollment()
    {
        var student = await _students.GetByUserIdAsync(User.GetUserId());
        if (student is null) return NotFound();

        ClassFormViewModel? classInfo = null;
        if (!string.IsNullOrWhiteSpace(student.ClassId))
            classInfo = await _classes.GetByIdAsync(student.ClassId);

        ViewBag.Student = student;
        ViewBag.ClassInfo = classInfo;
        ViewBag.PreviewUrl = _frm.GetPreviewFeedUrl() + "&t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ViewBag.FaceRegistered = await IsFaceRegisteredAsync(student.Id!);
        return View();
    }

    private async Task<bool> IsFaceRegisteredAsync(string studentId)
    {
        var profile = await _students.GetProfileAsync(User.GetUserId());
        return profile?.FaceRegistered == true;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CaptureFace()
    {
        var student = await _students.GetByUserIdAsync(User.GetUserId());
        if (student is null)
            return Json(new { ok = false, error = "Student profile not found." });

        if (string.IsNullOrWhiteSpace(student.ClassId))
            return Json(new { ok = false, error = "You are not assigned to a class. Contact your admin." });

        var frmClassId = await EnsureSyncedFrmClassAsync(student.ClassId);
        if (frmClassId is null)
            return Json(new { ok = false, error = "Could not sync with Face Recognition module. Make sure FRModule is running on port 8000." });

        var capture = await _frm.CaptureFaceAsync(frmClassId.Value);
        if (capture is null || !capture.Ok)
            return Json(new { ok = false, error = capture?.Error ?? "Face capture failed." });

        return Json(new
        {
            ok = true,
            token = capture.Token,
            samples = capture.Samples,
            preview = capture.Preview,
            duplicate = capture.Duplicate,
            frmClassId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterFace(string token, int frmClassId)
    {
        var student = await _students.GetByUserIdAsync(User.GetUserId());
        if (student is null)
            return Json(new { ok = false, error = "Student profile not found." });

        if (string.IsNullOrWhiteSpace(token))
            return Json(new { ok = false, error = "Capture token missing. Capture your face first." });

        var enroll = await _frm.EnrollFaceAsync(frmClassId, new FrmEnrollRequest
        {
            Token = token,
            Name = student.Name,
            RollNo = student.StudentId,
            Email = student.Email,
            Phone = student.Mobile,
            ExternalId = student.Id!
        });

        if (enroll is null || !enroll.Ok)
            return Json(new { ok = false, error = enroll?.Error ?? enroll?.Message ?? "Enrollment failed." });

        await _students.MarkFaceEnrolledAsync(student.Id!, enroll.FrmStudentId);
        return Json(new { ok = true, message = enroll.Message ?? "Face registered successfully." });
    }

    private async Task<int?> EnsureSyncedFrmClassAsync(string classId)
    {
        var detail = await _classes.GetDetailAsync(classId);
        if (detail?.Class.Id is null) return null;

        if (detail.Class.FrmClassId is int existing)
            return existing;

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
        if (result is null || !result.Ok) return null;

        await _classes.SetFrmClassIdAsync(classId, result.FrmClassId);
        return result.FrmClassId;
    }
}
