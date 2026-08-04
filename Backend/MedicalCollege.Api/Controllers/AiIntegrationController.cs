using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Api.Controllers;

/// <summary>
/// Extension points for future FastAPI face-recognition integration.
/// These endpoints intentionally return placeholders / readiness contracts.
/// </summary>
[ApiController]
[Route("api/ai")]
public class AiIntegrationController : ControllerBase
{
    private readonly IStudentService _students;
    private readonly IAttendanceService _attendance;

    public AiIntegrationController(IStudentService students, IAttendanceService attendance)
    {
        _students = students;
        _attendance = attendance;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new
    {
        status = "ready",
        facialRecognition = "not_implemented",
        integration = "fastapi_rest_planned",
        timestamp = DateTime.UtcNow
    });

    /// <summary>Future: proxy enroll request to FastAPI face service.</summary>
    [HttpPost("face/enroll/{studentId}")]
    public IActionResult EnrollFace(string studentId) => StatusCode(StatusCodes.Status501NotImplemented, new
    {
        message = "Face enrollment will call FastAPI POST /v1/enroll",
        studentId,
        plannedPayload = new { imageBase64 = "<base64>", studentId }
    });

    /// <summary>Future: recognize faces from camera frame.</summary>
    [HttpPost("face/recognize")]
    public IActionResult Recognize() => StatusCode(StatusCodes.Status501NotImplemented, new
    {
        message = "Recognition will call FastAPI POST /v1/recognize",
        plannedResponse = new { matches = Array.Empty<object>(), unknownCount = 0 }
    });

    /// <summary>Future: unknown face review queue.</summary>
    [HttpGet("face/unknown")]
    public IActionResult UnknownFaces() => Ok(new
    {
        items = Array.Empty<object>(),
        note = "Populated after InsightFace / FastAPI pipeline is connected"
    });

    /// <summary>Bridge: student directory for face module sync.</summary>
    [HttpGet("students")]
    public async Task<IActionResult> Students()
    {
        var page = await _students.SearchAsync(new StudentListFilter { Page = 1, PageSize = 500 });
        return Ok(page.Items.Select(s => new
        {
            s.Id,
            s.StudentId,
            s.Name,
            s.Department,
            s.Course,
            faceRegistered = false
        }));
    }

    [HttpGet("attendance/today")]
    public async Task<IActionResult> TodayAttendance()
    {
        var records = await _attendance.GetDailyAsync(DateTime.Today);
        return Ok(records);
    }
}
