using System.Globalization;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedicalCollege.Infrastructure.Frm;

/// <summary>
/// Pulls live FRModule IN/OUT into portal attendance.
/// Present / Partially Present is decided from TOTAL time stayed
/// (sum of each IN→OUT visit), NEVER from first-in to last-out wall-clock span.
/// </summary>
public class FrmAttendanceSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<FrmAttendanceSyncWorker> _logger;

    public FrmAttendanceSyncWorker(IServiceScopeFactory scopes, ILogger<FrmAttendanceSyncWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "FRM attendance sync cycle skipped");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task SyncOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var frm = scope.ServiceProvider.GetRequiredService<IFrmClient>();
        var classes = scope.ServiceProvider.GetRequiredService<IClassRepository>();
        var students = scope.ServiceProvider.GetRequiredService<IStudentRepository>();
        var attendance = scope.ServiceProvider.GetRequiredService<IAttendanceRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var erp = scope.ServiceProvider.GetRequiredService<IErpIntegrationService>();

        var allClasses = (await classes.GetAllAsync()).Where(c => c.IsActive && c.FrmClassId.HasValue).ToList();
        if (allClasses.Count == 0) return;

        var allStudents = await students.GetAllAsync();
        var today = DateTime.Today;
        var dayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var cls in allClasses)
        {
            var rows = await frm.GetClassAttendanceAsync(cls.FrmClassId!.Value, ct);
            if (rows is null || rows.Count == 0) continue;

            // Sum each visit's duration for today (gaps while OUT are excluded).
            var stayByKey = await BuildTotalStaySecondsAsync(frm, cls.FrmClassId!.Value, dayKey, ct);

            var classStudents = allStudents.Where(s => s.ClassId == cls.Id && s.IsActive).ToList();
            var existingToday = (await attendance.GetByDateAsync(today)).ToList();
            var minMinutes = EffectiveMinAttendanceMinutes(cls);

            foreach (var row in rows)
            {
                var student = classStudents.FirstOrDefault(s =>
                                  !string.IsNullOrWhiteSpace(row.ExternalId) &&
                                  s.Id.Equals(row.ExternalId, StringComparison.OrdinalIgnoreCase))
                              ?? classStudents.FirstOrDefault(s =>
                                  s.FrmStudentId == row.StudentId)
                              ?? classStudents.FirstOrDefault(s =>
                                  s.StudentId.Equals(row.RollNo, StringComparison.OrdinalIgnoreCase));

                if (student is null) continue;

                var staySeconds = ResolveStaySeconds(row, student, stayByKey);
                var durationSeconds = CapDurationSeconds(staySeconds, cls.MaxClassDurationMinutes);
                var durationLabel = FormatDuration(durationSeconds);

                var isPartial = IsPartiallyPresent(minMinutes, durationSeconds);
                var status = isPartial ? AttendanceStatus.PartialAbsent : AttendanceStatus.Present;
                var faceOut = string.Equals(row.Status, "OUT", StringComparison.OrdinalIgnoreCase);
                var lastOutDisplay = faceOut && !string.IsNullOrWhiteSpace(row.LastOut) && row.LastOut != "-"
                    ? row.LastOut
                    : null;
                var remark = isPartial
                    ? $"Face {row.Status}; First In {row.FirstIn}; Last Out {lastOutDisplay ?? "—"}; Total stay {durationLabel}; Partially present (< {minMinutes} min total)"
                    : $"Face {row.Status}; First In {row.FirstIn}; Last Out {lastOutDisplay ?? "—"}; Total stay {durationLabel}";

                var record = existingToday.FirstOrDefault(r => r.StudentId == student.Id && r.Date.Date == today);
                var isNew = record is null;
                var previousPartial = record?.Status == AttendanceStatus.PartialAbsent || (record?.EarlyLeave ?? false);

                if (isNew)
                {
                    record = new AttendanceRecord
                    {
                        StudentId = student.Id,
                        StudentCode = student.StudentId,
                        StudentName = student.Name,
                        Department = student.Department,
                        Course = student.Course,
                        Date = today,
                        Status = status,
                        MarkedBy = "FRModule",
                        Remarks = remark,
                        FirstIn = row.FirstIn,
                        LastOut = lastOutDisplay,
                        Duration = durationLabel,
                        DurationSeconds = durationSeconds,
                        EarlyLeave = isPartial,
                        Source = "FaceRecognition",
                        CreatedAt = DateTime.UtcNow
                    };
                    await attendance.AddAsync(record);
                    existingToday.Add(record);
                }
                else
                {
                    record!.Status = status;
                    record.StudentName = student.Name;
                    record.Remarks = remark;
                    record.FirstIn = row.FirstIn;
                    record.LastOut = lastOutDisplay;
                    record.Duration = durationLabel;
                    record.DurationSeconds = durationSeconds;
                    record.EarlyLeave = isPartial;
                    record.Source = "FaceRecognition";
                    record.MarkedBy = "FRModule";
                    await attendance.UpdateAsync(record);
                }

                if (isPartial && !previousPartial && minMinutes is >= 1)
                {
                    if (!string.IsNullOrWhiteSpace(cls.AdminUserId))
                    {
                        await notifications.CreateAsync(
                            cls.AdminUserId!,
                            "Partially present",
                            $"{student.Name} ({student.StudentId}) total stay {durationLabel} in {cls.Name} (minimum {minMinutes} min for Present).",
                            NotificationType.ParentEarlyLeaveAlert,
                            $"/Admin/AttendanceLog/{cls.Id}",
                            ct,
                            cls.Id,
                            cls.Name);
                    }
                }

                await erp.PushAttendanceAsync(record!, student, ct);
            }
        }
    }

    /// <summary>
    /// Sum session lengths for each student today. Gaps between OUT and next IN are not counted.
    /// </summary>
    private static async Task<Dictionary<string, int>> BuildTotalStaySecondsAsync(
        IFrmClient frm, int frmClassId, string dayKey, CancellationToken ct)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sessions = await frm.GetClassSessionsAsync(frmClassId, ct);
        if (sessions is null || sessions.Count == 0)
            return map;

        var now = DateTime.Now;
        foreach (var s in sessions.Where(x => string.Equals(x.Date, dayKey, StringComparison.Ordinal)))
        {
            var secs = SessionDurationSeconds(s.EntryTs, s.ExitTs, now);
            if (secs <= 0) continue;

            void Add(string? key)
            {
                if (string.IsNullOrWhiteSpace(key)) return;
                map[key] = map.TryGetValue(key, out var cur) ? cur + secs : secs;
            }

            Add(s.ExternalId);
            Add(s.RollNo);
        }

        return map;
    }

    private static int ResolveStaySeconds(
        FrmAttendanceRow row,
        Student student,
        IReadOnlyDictionary<string, int> stayByKey)
    {
        // Prefer summed visit durations from session list (excludes OUT gaps).
        if (!string.IsNullOrWhiteSpace(row.ExternalId) &&
            stayByKey.TryGetValue(row.ExternalId, out var byExt) && byExt > 0)
            return byExt;
        if (!string.IsNullOrWhiteSpace(student.Id) &&
            stayByKey.TryGetValue(student.Id, out var byId) && byId > 0)
            return byId;
        if (!string.IsNullOrWhiteSpace(row.RollNo) &&
            stayByKey.TryGetValue(row.RollNo, out var byRoll) && byRoll > 0)
            return byRoll;
        if (!string.IsNullOrWhiteSpace(student.StudentId) &&
            stayByKey.TryGetValue(student.StudentId, out var byCode) && byCode > 0)
            return byCode;

        // Fallback: FR dashboard already sums sessions into time_in_class_seconds.
        return Math.Max(0, row.TimeInClassSeconds);
    }

    private static int SessionDurationSeconds(string? entryTs, string? exitTs, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(entryTs))
            return 0;

        if (!DateTime.TryParse(entryTs, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) &&
            !DateTime.TryParse(entryTs, out start))
            return 0;

        DateTime end;
        if (string.IsNullOrWhiteSpace(exitTs) || exitTs == "-")
            end = now;
        else if (!DateTime.TryParse(exitTs, CultureInfo.InvariantCulture, DateTimeStyles.None, out end) &&
                 !DateTime.TryParse(exitTs, out end))
            end = now;

        return Math.Max(0, (int)(end - start).TotalSeconds);
    }

    /// <summary>
    /// Below minimum total stay minutes ⇒ Partially Present (not Present).
    /// </summary>
    internal static bool IsPartiallyPresent(int? minMinutes, int durationSeconds)
    {
        if (minMinutes is null or < 1)
            return false;
        var attendedMinutes = durationSeconds / 60.0;
        return attendedMinutes < minMinutes.Value;
    }

    /// <summary>
    /// Admin min minutes if set; otherwise 50% of max class duration; otherwise no threshold.
    /// </summary>
    internal static int? EffectiveMinAttendanceMinutes(ClassRoom cls)
    {
        if (cls.MinAttendanceMinutes is >= 1)
            return cls.MinAttendanceMinutes.Value;
        if (cls.MaxClassDurationMinutes is > 0)
            return Math.Max(1, (int)Math.Ceiling(cls.MaxClassDurationMinutes.Value * 0.5));
        return null;
    }

    private static int CapDurationSeconds(int seconds, int? maxMinutes)
    {
        if (seconds < 0) return 0;
        if (maxMinutes is > 0)
            return Math.Min(seconds, maxMinutes.Value * 60);
        return seconds;
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds <= 0) return "0s";
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1)
            return ts.Seconds > 0 ? $"{(int)ts.TotalMinutes}m {ts.Seconds}s" : $"{(int)ts.TotalMinutes}m";
        return $"{ts.Seconds}s";
    }
}
