using MedicalCollege.Application.Common;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace MedicalCollege.Application.Services;

/// <summary>
/// Parent alerts: log + optional email. Respects ParentEmailUnsubscribed.
/// </summary>
public class ParentNotificationService : IParentNotificationService
{
    private readonly IParentAlertRepository _alerts;
    private readonly INotificationService _notifications;
    private readonly IUserRepository _users;
    private readonly IStudentRepository _students;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;

    public ParentNotificationService(
        IParentAlertRepository alerts,
        INotificationService notifications,
        IUserRepository users,
        IStudentRepository students,
        IEmailService email,
        IConfiguration config)
    {
        _alerts = alerts;
        _notifications = notifications;
        _users = users;
        _students = students;
        _email = email;
        _config = config;
    }

    public Task<ServiceResult> NotifyAbsenceAsync(Student student, string className, DateTime date, CancellationToken ct = default)
    {
        var msg = $"{student.Name} ({student.StudentId}) was marked Absent for {className} on {date:dd MMM yyyy}.";
        return SendAsync(student, className, "Absence", msg, NotificationType.ParentAbsenceAlert, notifyAdminInbox: true, ct);
    }

    public Task<ServiceResult> NotifyEarlyLeaveAsync(Student student, string className, string? outTime, CancellationToken ct = default)
    {
        var msg = $"{student.Name} ({student.StudentId}) left {className} early at {outTime ?? "unknown time"} (before class end).";
        return SendAsync(student, className, "EarlyLeave", msg, NotificationType.ParentEarlyLeaveAlert, notifyAdminInbox: true, ct);
    }

    public Task<ServiceResult> NotifyAttendanceUpdateAsync(
        Student student, string className, string status, string? inTime, string? outTime, CancellationToken ct = default)
    {
        var msg = $"{student.Name} ({student.StudentId}) attendance at {className}: {status}. In: {inTime ?? "—"}, Out: {outTime ?? "—"}.";
        var typeLabel = status.Equals("IN", StringComparison.OrdinalIgnoreCase) ? "CheckedIn"
            : status.Equals("OUT", StringComparison.OrdinalIgnoreCase) ? "CheckedOut"
            : "Update";
        return SendAsync(student, className, typeLabel, msg, NotificationType.ParentAttendanceUpdate, notifyAdminInbox: false, ct);
    }

    public Task<IReadOnlyList<ParentAlert>> GetRecentAsync(int take = 100, CancellationToken ct = default) =>
        _alerts.GetRecentAsync(take);

    public async Task<ServiceResult> UnsubscribeParentEmailAsync(string studentId, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(studentId);
        if (student is null)
            return ServiceResult.Fail("Student not found.");

        student.ParentEmailUnsubscribed = true;
        student.UpdatedAt = DateTime.UtcNow;
        await _students.UpdateAsync(student);
        return ServiceResult.Ok("Parent email alerts have been unsubscribed.");
    }

    private async Task<ServiceResult> SendAsync(
        Student student,
        string className,
        string alertType,
        string message,
        NotificationType adminType,
        bool notifyAdminInbox,
        CancellationToken ct)
    {
        var recent = await _alerts.GetByStudentAsync(student.Id);
        if (recent.Any(a =>
                a.AlertType.Equals(alertType, StringComparison.OrdinalIgnoreCase) &&
                a.Message.Equals(message, StringComparison.Ordinal) &&
                a.CreatedAt > DateTime.UtcNow.AddMinutes(-30)))
            return ServiceResult.Ok("Duplicate parent alert skipped.");

        var hasContact = !string.IsNullOrWhiteSpace(student.GuardianPhone)
                         || !string.IsNullOrWhiteSpace(student.GuardianEmail);

        var emailEnabled = string.Equals(_config["ParentAlerts:EnableEmail"], "true", StringComparison.OrdinalIgnoreCase);
        var deliveryNote = "Logged on-premises.";
        var delivered = false;
        var channel = "Log";

        if (emailEnabled && !string.IsNullOrWhiteSpace(student.GuardianEmail) && !student.ParentEmailUnsubscribed)
        {
            var baseUrl = (_config["ParentAlerts:PortalBaseUrl"] ?? "http://127.0.0.1:5148").TrimEnd('/');
            var unsubUrl = $"{baseUrl}/Account/UnsubscribeParentAlerts?token={Uri.EscapeDataString(student.Id)}";
            var html = $@"
<p>Dear Parent/Guardian,</p>
<p>{System.Net.WebUtility.HtmlEncode(message)}</p>
<p style='font-size:12px;color:#666;'>
  If you no longer wish to receive these alerts,
  <a href=""{unsubUrl}"">unsubscribe here</a>.
</p>";
            var (sent, detail) = await _email.SendAsync(
                student.GuardianEmail!,
                $"Attendance alert: {alertType} — {student.Name}",
                html,
                ct);
            delivered = sent;
            channel = "Email";
            deliveryNote = sent ? $"Email sent to {student.GuardianEmail}." : $"Email not sent: {detail}";
        }
        else if (student.ParentEmailUnsubscribed)
        {
            deliveryNote = "Parent unsubscribed from email alerts.";
        }
        else if (!hasContact)
        {
            deliveryNote = "Logged on-premises. No guardian phone/email on student record.";
        }
        else
        {
            deliveryNote = "Logged on-premises. Enable ParentAlerts:EnableEmail for live email delivery.";
        }

        await _alerts.AddAsync(new ParentAlert
        {
            StudentId = student.Id,
            StudentName = student.Name,
            ClassId = student.ClassId,
            ClassName = className,
            GuardianName = student.GuardianName,
            GuardianPhone = student.GuardianPhone,
            GuardianEmail = student.GuardianEmail,
            Channel = channel,
            AlertType = alertType,
            Message = message,
            Delivered = delivered,
            DeliveryNote = deliveryNote,
            CreatedAt = DateTime.UtcNow
        });

        if (!notifyAdminInbox)
            return ServiceResult.Ok("Parent alert recorded.");

        var classQuery = string.IsNullOrWhiteSpace(student.ClassId) ? "" : $"?classId={Uri.EscapeDataString(student.ClassId)}";
        var admins = await _users.GetByRoleAsync(UserRole.Admin);
        foreach (var admin in admins.Where(a => a.IsActive))
        {
            await _notifications.CreateAsync(
                admin.Id,
                $"Parent alert: {alertType}",
                message + (hasContact
                    ? $" · Guardian: {student.GuardianName ?? "—"} / {student.GuardianPhone ?? student.GuardianEmail}"
                    : " (No guardian contact on file.)"),
                adminType,
                $"/Admin/ParentAlerts{classQuery}",
                ct,
                student.ClassId,
                className);
        }

        return ServiceResult.Ok("Parent alert recorded.");
    }
}
