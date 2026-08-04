using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Web.Controllers;

[Authorize(Roles = "Admin")]
public class ReportsController : Controller
{
    private readonly IStudentService _students;
    private readonly IAttendanceService _attendance;

    public ReportsController(IStudentService students, IAttendanceService attendance)
    {
        _students = students;
        _attendance = attendance;
    }

    public IActionResult Index() => View();

    public async Task<IActionResult> Attendance()
    {
        ViewBag.Stats = await _attendance.GetStatsAsync();
        ViewBag.Records = await _attendance.GetDailyAsync(DateTime.Today);
        return View();
    }

    public async Task<IActionResult> Students()
    {
        var result = await _students.SearchAsync(new StudentListFilter { Page = 1, PageSize = 500 });
        return View(result);
    }

    public async Task<IActionResult> Departments()
    {
        return View(await GetDepartmentGroupsAsync());
    }

    public async Task<IActionResult> Courses()
    {
        return View(await GetCourseGroupsAsync());
    }

    [HttpGet]
    public async Task<IActionResult> ExportAttendance(string format = "excel")
    {
        var stats = await _attendance.GetStatsAsync();
        var records = await _attendance.GetDailyAsync(DateTime.Today);
        var stamp = DateTime.Today.ToString("yyyyMMdd");

        if (IsPdf(format))
        {
            var pdf = ReportExportBuilder.AttendancePdf(records, stats);
            return File(pdf, "application/pdf", $"attendance-report-{stamp}.pdf");
        }

        var csv = ReportExportBuilder.AttendanceCsv(records, stats);
        return File(csv, "text/csv", $"attendance-report-{stamp}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportStudents(string format = "excel")
    {
        var result = await _students.SearchAsync(new StudentListFilter { Page = 1, PageSize = 5000 });
        var stamp = DateTime.Today.ToString("yyyyMMdd");

        if (IsPdf(format))
        {
            var pdf = ReportExportBuilder.StudentsPdf(result.Items);
            return File(pdf, "application/pdf", $"students-report-{stamp}.pdf");
        }

        var csv = ReportExportBuilder.StudentsCsv(result.Items);
        return File(csv, "text/csv", $"students-report-{stamp}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportDepartments(string format = "excel")
    {
        var groups = await GetDepartmentGroupsAsync();
        var stamp = DateTime.Today.ToString("yyyyMMdd");

        if (IsPdf(format))
        {
            var pdf = ReportExportBuilder.ChartPdf("Department Report", "Department", groups);
            return File(pdf, "application/pdf", $"departments-report-{stamp}.pdf");
        }

        var csv = ReportExportBuilder.ChartCsv("Department", groups);
        return File(csv, "text/csv", $"departments-report-{stamp}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportCourses(string format = "excel")
    {
        var groups = await GetCourseGroupsAsync();
        var stamp = DateTime.Today.ToString("yyyyMMdd");

        if (IsPdf(format))
        {
            var pdf = ReportExportBuilder.ChartPdf("Course Report", "Course", groups);
            return File(pdf, "application/pdf", $"courses-report-{stamp}.pdf");
        }

        var csv = ReportExportBuilder.ChartCsv("Course", groups);
        return File(csv, "text/csv", $"courses-report-{stamp}.csv");
    }

    private async Task<List<ChartPoint>> GetDepartmentGroupsAsync()
    {
        var result = await _students.SearchAsync(new StudentListFilter { Page = 1, PageSize = 5000 });
        return result.Items
            .GroupBy(s => string.IsNullOrWhiteSpace(s.Department) ? "Unassigned" : s.Department)
            .Select(g => new ChartPoint { Label = g.Key, Value = g.Count() })
            .OrderByDescending(g => g.Value)
            .ToList();
    }

    private async Task<List<ChartPoint>> GetCourseGroupsAsync()
    {
        var result = await _students.SearchAsync(new StudentListFilter { Page = 1, PageSize = 5000 });
        return result.Items
            .GroupBy(s => string.IsNullOrWhiteSpace(s.Course) ? "Unassigned" : s.Course)
            .Select(g => new ChartPoint { Label = g.Key, Value = g.Count() })
            .OrderByDescending(g => g.Value)
            .ToList();
    }

    private static bool IsPdf(string? format) =>
        string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase);
}
