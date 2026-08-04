using MedicalCollege.Application.Interfaces;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Web.Controllers;

[ApiController]
[Route("api/frm")]
[AllowAnonymous]
public class FrmEventsController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IClassRepository _classes;
    private readonly IStudentRepository _students;
    private readonly IUserRepository _users;
    private readonly INotificationService _notifications;
    private readonly ILogger<FrmEventsController> _logger;

    public FrmEventsController(
        IConfiguration config,
        IClassRepository classes,
        IStudentRepository students,
        IUserRepository users,
        INotificationService notifications,
        ILogger<FrmEventsController> logger)
    {
        _config = config;
        _classes = classes;
        _students = students;
        _users = users;
        _notifications = notifications;
        _logger = logger;
    }

    public sealed class WrongClassDto
    {
        public int CameraFrmClassId { get; set; }
        public string? CameraClassExternalId { get; set; }
        public int StudentFrmId { get; set; }
        public string? StudentExternalId { get; set; }
        public string? StudentName { get; set; }
        public string? RollNo { get; set; }
        public string? HomeClassName { get; set; }
    }

    [HttpPost("wrong-class")]
    public async Task<IActionResult> WrongClass([FromBody] WrongClassDto? dto, CancellationToken ct)
    {
        var expected = _config["Frm:ApiKey"] ?? string.Empty;
        var key = Request.Headers["X-Api-Key"].FirstOrDefault()
                  ?? Request.Query["api_key"].FirstOrDefault()
                  ?? string.Empty;
        if (string.IsNullOrWhiteSpace(expected) ||
            !string.Equals(key, expected, StringComparison.Ordinal))
            return Unauthorized(new { ok = false, error = "Unauthorized" });

        if (dto is null)
            return BadRequest(new { ok = false, error = "Body required" });

        var classes = await _classes.GetAllAsync();
        var cameraClass = classes.FirstOrDefault(c =>
                               c.FrmClassId == dto.CameraFrmClassId)
                           ?? classes.FirstOrDefault(c =>
                               !string.IsNullOrWhiteSpace(dto.CameraClassExternalId) &&
                               c.Id.Equals(dto.CameraClassExternalId, StringComparison.OrdinalIgnoreCase));

        var allStudents = await _students.GetAllAsync();
        var visitor = allStudents.FirstOrDefault(s =>
                          !string.IsNullOrWhiteSpace(dto.StudentExternalId) &&
                          s.Id.Equals(dto.StudentExternalId, StringComparison.OrdinalIgnoreCase))
                      ?? allStudents.FirstOrDefault(s =>
                          s.FrmStudentId == dto.StudentFrmId)
                      ?? allStudents.FirstOrDefault(s =>
                          !string.IsNullOrWhiteSpace(dto.RollNo) &&
                          s.StudentId.Equals(dto.RollNo, StringComparison.OrdinalIgnoreCase));

        var visitorName = visitor?.Name ?? dto.StudentName ?? "Unknown student";
        var visitorCode = visitor?.StudentId ?? dto.RollNo ?? dto.StudentFrmId.ToString();
        var homeClass = visitor?.ClassId is null
            ? null
            : classes.FirstOrDefault(c => c.Id == visitor.ClassId);
        var homeName = homeClass?.Name ?? dto.HomeClassName ?? "another class";
        var cameraName = cameraClass?.Name ?? $"FRM class {dto.CameraFrmClassId}";

        var message =
            $"{visitorName} ({visitorCode}) from {homeName} appeared at the camera for {cameraName}. Attendance was not marked.";

        // Notify only the class owner (or one active admin) — not every admin (avoids inbox flood).
        var adminIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(cameraClass?.AdminUserId))
            adminIds.Add(cameraClass.AdminUserId!);
        else
        {
            var fallback = (await _users.GetByRoleAsync(UserRole.Admin)).FirstOrDefault(a => a.IsActive);
            if (fallback is not null)
                adminIds.Add(fallback.Id);
        }

        foreach (var adminId in adminIds)
        {
            await _notifications.CreateAsync(
                adminId,
                "Wrong class face detected",
                message,
                NotificationType.SecurityAlert,
                cameraClass is null ? "/Admin" : $"/Admin/Notifications?classId={Uri.EscapeDataString(cameraClass.Id)}",
                ct,
                cameraClass?.Id,
                cameraClass?.Name ?? cameraName);
        }

        _logger.LogInformation("Wrong-class alert: {Message}", message);
        return Ok(new { ok = true, notified = adminIds.Count });
    }
}
