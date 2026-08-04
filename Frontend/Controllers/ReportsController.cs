using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
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
        var result = await _students.SearchAsync(new StudentListFilter { Page = 1, PageSize = 100 });
        return View(result);
    }

    public async Task<IActionResult> Departments()
    {
        var result = await _students.SearchAsync(new StudentListFilter { Page = 1, PageSize = 100 });
        var groups = result.Items
            .GroupBy(s => s.Department)
            .Select(g => new ChartPoint { Label = g.Key, Value = g.Count() })
            .ToList();
        return View(groups);
    }

    public async Task<IActionResult> Courses()
    {
        var result = await _students.SearchAsync(new StudentListFilter { Page = 1, PageSize = 100 });
        var groups = result.Items
            .GroupBy(s => s.Course)
            .Select(g => new ChartPoint { Label = g.Key, Value = g.Count() })
            .ToList();
        return View(groups);
    }
}
