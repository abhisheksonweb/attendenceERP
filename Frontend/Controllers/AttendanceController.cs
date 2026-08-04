using System.Globalization;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Infrastructure.Frm;
using MedicalCollege.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AttendanceController : Controller
{
    private readonly IAttendanceService _attendance;
    private readonly IClassService _classes;
    private readonly IFrmClient _frm;

    public AttendanceController(
        IAttendanceService attendance,
        IClassService classes,
        IFrmClient frm)
    {
        _attendance = attendance;
        _classes = classes;
        _frm = frm;
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Index(string? date, string? status)
    {
        // Bind as string so yyyy-MM-dd from <input type="date"> is reliable across cultures.
        var selected = DateTime.TryParseExact(
                date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
            ? parsed.Date
            : DateTime.Today;

        var records = await _attendance.GetDailyAsync(selected);
        var statusFilter = (status ?? "").Trim();
        if (!string.IsNullOrEmpty(statusFilter))
        {
            records = statusFilter.Equals("Present", StringComparison.OrdinalIgnoreCase)
                ? records.Where(r =>
                        r.Status.Equals("Present", StringComparison.OrdinalIgnoreCase) ||
                        r.Status.Equals("Late", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : statusFilter.Equals("Absent", StringComparison.OrdinalIgnoreCase)
                    ? records.Where(r =>
                            r.Status.Equals("Absent", StringComparison.OrdinalIgnoreCase) ||
                            AttendanceDisplay.IsPartiallyPresent(r.Status))
                        .ToList()
                    : records.Where(r => r.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase))
                        .ToList();
        }

        var dayKey = selected.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var visitLogs = await LoadVisitLogsForDateAsync(dayKey);

        ViewBag.Date = selected;
        ViewBag.StatusFilter = statusFilter;
        ViewBag.Stats = await _attendance.GetDailyStatsAsync(selected);
        ViewBag.VisitLogs = visitLogs;
        ViewBag.VisitCounts = visitLogs
            .GroupBy(v => v.RollNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return View(records);
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Monthly(int? year, int? month)
    {
        var y = year ?? DateTime.Today.Year;
        var m = month ?? DateTime.Today.Month;
        var records = await _attendance.GetMonthlyAsync(y, m);
        ViewBag.Year = y;
        ViewBag.Month = m;
        ViewBag.Stats = await _attendance.GetMonthlyStatsAsync(y, m);
        return View(records);
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Calendar(int? year, int? month)
    {
        var y = year ?? DateTime.Today.Year;
        var m = month ?? DateTime.Today.Month;
        var records = await _attendance.GetMonthlyAsync(y, m);
        ViewBag.Year = y;
        ViewBag.Month = m;
        return View(records);
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Statistics()
    {
        var stats = await _attendance.GetStatsAsync();
        return View(stats);
    }

    private async Task<List<DailyVisitLogRow>> LoadVisitLogsForDateAsync(string dayKey)
    {
        var classes = await _classes.GetAllAsync();
        var logs = new List<DailyVisitLogRow>();

        foreach (var cls in classes.Where(c => c.FrmClassId.HasValue))
        {
            var sessions = await _frm.GetClassSessionsAsync(cls.FrmClassId!.Value)
                           ?? Array.Empty<FrmSessionRow>();

            foreach (var s in sessions.Where(s => string.Equals(s.Date, dayKey, StringComparison.Ordinal)))
            {
                logs.Add(new DailyVisitLogRow
                {
                    ClassName = cls.Name,
                    Name = s.Name,
                    RollNo = s.RollNo,
                    EntryTime = s.EntryTime,
                    ExitTime = s.ExitTime,
                    EntryTs = s.EntryTs,
                    Duration = s.Duration,
                    Status = s.Status
                });
            }
        }

        // Chronological order, then number each student's visits for the day.
        logs = logs
            .OrderBy(l => l.EntryTs ?? l.EntryTime ?? "")
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in logs.GroupBy(l => l.RollNo, StringComparer.OrdinalIgnoreCase))
        {
            var n = 1;
            foreach (var row in group.OrderBy(l => l.EntryTs ?? l.EntryTime ?? ""))
                row.VisitNo = n++;
        }

        return logs;
    }
}

public class DailyVisitLogRow
{
    public string ClassName { get; set; } = "";
    public string Name { get; set; } = "";
    public string RollNo { get; set; } = "";
    public string? EntryTime { get; set; }
    public string? ExitTime { get; set; }
    public string? EntryTs { get; set; }
    public string? Duration { get; set; }
    public string Status { get; set; } = "OUT";
    public int VisitNo { get; set; }
}
