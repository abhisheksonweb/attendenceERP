using MedicalCollege.Application.Interfaces;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedicalCollege.Infrastructure.Frm;

/// <summary>
/// Parent alerts: absence only. Runs once per day at configured time (default 18:00).
/// </summary>
public class AbsenceAlertWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<AbsenceAlertWorker> _logger;
    private string? _lastRunDate;

    public AbsenceAlertWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<AbsenceAlertWorker> logger)
    {
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDailyAbsenceAlertsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Daily absence alert cycle skipped");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task RunDailyAbsenceAlertsAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var todayKey = now.ToString("yyyy-MM-dd");
        var timeText = (_config["ParentAlerts:DailyMissedClassTime"] ?? "18:00").Trim();
        if (!TimeSpan.TryParse(timeText, out var runAt))
            runAt = new TimeSpan(18, 0, 0);

        // Only at/after 6:00 PM local time, once per calendar day.
        if (now.TimeOfDay < runAt)
            return;
        if (string.Equals(_lastRunDate, todayKey, StringComparison.Ordinal))
            return;

        using var scope = _scopes.CreateScope();
        var classes = scope.ServiceProvider.GetRequiredService<IClassRepository>();
        var students = scope.ServiceProvider.GetRequiredService<IStudentRepository>();
        var attendance = scope.ServiceProvider.GetRequiredService<IAttendanceRepository>();
        var parents = scope.ServiceProvider.GetRequiredService<IParentNotificationService>();

        var today = now.Date;
        var allStudents = await students.GetAllAsync();
        var todayRecords = await attendance.GetByDateAsync(today);
        var recentAlerts = await parents.GetRecentAsync(2000, ct);
        var sent = 0;

        foreach (var cls in (await classes.GetAllAsync()).Where(c => c.IsActive))
        {
            var classStudents = allStudents.Where(s => s.ClassId == cls.Id && s.IsActive).ToList();
            foreach (var student in classStudents)
            {
                var record = todayRecords.FirstOrDefault(r => r.StudentId == student.Id);
                var isPresent = record is not null &&
                    (record.Status == AttendanceStatus.Present || record.Status == AttendanceStatus.Late);

                if (isPresent)
                    continue;

                // Sundays are week off — no parent absence alert.
                if (today.DayOfWeek == DayOfWeek.Sunday)
                    continue;

                // Skip if an absence alert was already logged today for this student.
                var already = recentAlerts.Any(a =>
                    a.StudentId == student.Id &&
                    a.AlertType.Equals("Absence", StringComparison.OrdinalIgnoreCase) &&
                    a.CreatedAt.ToLocalTime().Date == today);
                if (already)
                    continue;

                await parents.NotifyAbsenceAsync(student, cls.Name, today, ct);
                sent++;
            }
        }

        _lastRunDate = todayKey;
        _logger.LogInformation(
            "6 PM parent absence alerts finished for {Date}. Notifications attempted: {Count}",
            todayKey,
            sent);
    }
}
