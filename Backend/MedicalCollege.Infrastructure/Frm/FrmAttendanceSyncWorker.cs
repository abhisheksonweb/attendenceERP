using MedicalCollege.Application.Interfaces;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedicalCollege.Infrastructure.Frm;

/// <summary>
/// Pulls live FRModule IN/OUT into portal attendance, applies min-attendance
/// (partial absent) rules, and optionally pushes to college ERP.
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

        foreach (var cls in allClasses)
        {
            var rows = await frm.GetClassAttendanceAsync(cls.FrmClassId!.Value, ct);
            if (rows is null || rows.Count == 0) continue;

            var classStudents = allStudents.Where(s => s.ClassId == cls.Id && s.IsActive).ToList();
            var existingToday = (await attendance.GetByDateAsync(today)).ToList();

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

                var durationSeconds = CapDurationSeconds(row.TimeInClassSeconds, cls.MaxClassDurationMinutes);
                var durationLabel = string.IsNullOrWhiteSpace(row.TimeInClass)
                    ? FormatDuration(durationSeconds)
                    : row.TimeInClass;
                if (cls.MaxClassDurationMinutes is > 0 &&
                    durationSeconds >= cls.MaxClassDurationMinutes.Value * 60)
                {
                    durationLabel = FormatDuration(durationSeconds);
                }

                var isPartial = IsPartialAbsent(cls, row.Status, durationSeconds);
                var status = isPartial ? AttendanceStatus.PartialAbsent : AttendanceStatus.Present;
                var remark = isPartial
                    ? $"Face {row.Status}; In {row.FirstIn}; Out {row.LastOut}; Duration {durationLabel}; Partial absent (< {cls.MinAttendanceMinutes} min)"
                    : $"Face {row.Status}; In {row.FirstIn}; Out {row.LastOut}; Duration {durationLabel}";

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
                        LastOut = row.LastOut,
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
                    // While still IN, keep Present even if current elapsed is under minimum.
                    // After OUT, apply partial-absent rule.
                    record!.Status = status;
                    record.StudentName = student.Name;
                    record.Remarks = remark;
                    record.FirstIn = row.FirstIn;
                    record.LastOut = row.LastOut;
                    record.Duration = durationLabel;
                    record.DurationSeconds = durationSeconds;
                    record.EarlyLeave = isPartial || record.EarlyLeave;
                    record.Source = "FaceRecognition";
                    record.MarkedBy = "FRModule";
                    await attendance.UpdateAsync(record);
                }

                if (isPartial && !previousPartial)
                {
                    if (!string.IsNullOrWhiteSpace(cls.AdminUserId))
                    {
                        await notifications.CreateAsync(
                            cls.AdminUserId!,
                            "Partial absence detected",
                            $"{student.Name} ({student.StudentId}) attended {durationLabel} in {cls.Name} (minimum {cls.MinAttendanceMinutes} min).",
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
    /// After check-out, attended minutes below MinAttendanceMinutes → partial absent.
    /// While still IN, do not flag (student may still meet the minimum).
    /// </summary>
    internal static bool IsPartialAbsent(ClassRoom cls, string faceStatus, int durationSeconds)
    {
        if (!faceStatus.Equals("OUT", StringComparison.OrdinalIgnoreCase))
            return false;
        if (cls.MinAttendanceMinutes is null or < 1)
            return false;
        var attendedMinutes = durationSeconds / 60.0;
        return attendedMinutes < cls.MinAttendanceMinutes.Value;
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
        if (seconds <= 0) return "0m";
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m";
    }
}
