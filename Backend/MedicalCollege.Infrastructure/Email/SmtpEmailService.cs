using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MedicalCollege.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace MedicalCollege.Infrastructure.Email;

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<(bool Sent, string Detail)> SendStudentWelcomeAsync(
        string toStudentEmail,
        string studentName,
        string studentId,
        string temporaryPassword,
        string fromAdminEmail,
        string fromAdminName,
        CancellationToken cancellationToken = default)
    {
        var loginUrl = _config["Smtp:LoginUrl"] ?? "http://127.0.0.1:5148/Account/Login";
        var adminLabel = string.IsNullOrWhiteSpace(fromAdminName) ? "Administrator" : fromAdminName.Trim();
        var adminMail = (fromAdminEmail ?? "").Trim();
        var subject = "Your Medical College Attendance login credentials";
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#1a1a1a;max-width:560px">
              <h2 style="margin:0 0 12px;color:#0f4c81">Welcome, {WebUtility.HtmlEncode(studentName)}</h2>
              <p>Your student account was created by <strong>{WebUtility.HtmlEncode(adminLabel)}</strong>
                 {(string.IsNullOrWhiteSpace(adminMail) ? "" : $"({WebUtility.HtmlEncode(adminMail)})")}.</p>
              <p>Use the credentials below to sign in to the attendance portal:</p>
              <table style="border-collapse:collapse;margin:16px 0;width:100%;background:#f6f8fb;border-radius:8px">
                <tr>
                  <td style="padding:12px 16px;color:#555;width:40%">Student ID</td>
                  <td style="padding:12px 16px"><strong style="font-size:16px">{WebUtility.HtmlEncode(studentId)}</strong></td>
                </tr>
                <tr>
                  <td style="padding:12px 16px;color:#555;border-top:1px solid #e5eaf0">Login Email (User ID)</td>
                  <td style="padding:12px 16px;border-top:1px solid #e5eaf0"><strong style="font-size:16px">{WebUtility.HtmlEncode(toStudentEmail)}</strong></td>
                </tr>
                <tr>
                  <td style="padding:12px 16px;color:#555;border-top:1px solid #e5eaf0">Temporary Password</td>
                  <td style="padding:12px 16px;border-top:1px solid #e5eaf0"><strong style="font-size:16px;letter-spacing:0.5px">{WebUtility.HtmlEncode(temporaryPassword)}</strong></td>
                </tr>
              </table>
              <p style="margin:20px 0">
                <a href="{WebUtility.HtmlEncode(loginUrl)}"
                   style="display:inline-block;background:#0f4c81;color:#fff;text-decoration:none;padding:10px 18px;border-radius:6px;font-weight:600">
                  Sign in to portal
                </a>
              </p>
              <p style="color:#666;font-size:13px;margin-top:24px">
                Please change your password after first login. Do not share these credentials with anyone.
              </p>
            </div>
            """;

        // From = admin who created the student; To = student.
        return SendAsync(
            toStudentEmail,
            subject,
            html,
            cancellationToken,
            fromEmail: adminMail,
            fromName: adminLabel,
            replyToEmail: adminMail);
    }

    public Task<(bool Sent, string Detail)> SendPasswordResetAsync(
        string toEmail,
        string studentName,
        string temporaryPassword,
        string? fromAdminEmail = null,
        string? fromAdminName = null,
        CancellationToken cancellationToken = default)
    {
        var loginUrl = _config["Smtp:LoginUrl"] ?? "http://127.0.0.1:5148/Account/Login";
        var adminLabel = string.IsNullOrWhiteSpace(fromAdminName) ? "Administrator" : fromAdminName.Trim();
        var subject = "Your attendance password was reset";
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#1a1a1a">
              <h2 style="margin:0 0 12px">Password reset</h2>
              <p>Hello {WebUtility.HtmlEncode(studentName)}, <strong>{WebUtility.HtmlEncode(adminLabel)}</strong> reset your password.</p>
              <table style="border-collapse:collapse;margin:16px 0">
                <tr><td style="padding:4px 12px 4px 0;color:#555">Login (email)</td><td><strong>{WebUtility.HtmlEncode(toEmail)}</strong></td></tr>
                <tr><td style="padding:4px 12px 4px 0;color:#555">New temporary password</td><td><strong>{WebUtility.HtmlEncode(temporaryPassword)}</strong></td></tr>
              </table>
              <p>Sign in here: <a href="{WebUtility.HtmlEncode(loginUrl)}">{WebUtility.HtmlEncode(loginUrl)}</a></p>
              <p style="color:#666;font-size:13px">Change this password after you sign in.</p>
            </div>
            """;
        return SendAsync(
            toEmail,
            subject,
            html,
            cancellationToken,
            fromEmail: fromAdminEmail,
            fromName: fromAdminName,
            replyToEmail: fromAdminEmail);
    }

    public Task<(bool Sent, string Detail)> SendParentAbsenceAlertAsync(
        string toGuardianEmail,
        string? guardianName,
        string studentName,
        string studentId,
        string className,
        string alertType,
        string message,
        DateTime alertDate,
        string unsubscribeUrl,
        CancellationToken cancellationToken = default)
    {
        var greeting = string.IsNullOrWhiteSpace(guardianName)
            ? "Dear Parent/Guardian"
            : $"Dear {guardianName.Trim()}";
        var typeLabel = string.IsNullOrWhiteSpace(alertType) ? "Attendance" : alertType.Trim();
        var isAbsence = typeLabel.Equals("Absence", StringComparison.OrdinalIgnoreCase);
        var heading = isAbsence ? "Absence alert" : $"{typeLabel} alert";
        var statusText = isAbsence ? "Absent" : typeLabel;
        var subject = isAbsence
            ? $"Absence alert — {studentName} ({studentId}) — {alertDate:dd MMM yyyy}"
            : $"Attendance alert: {typeLabel} — {studentName}";

        var dailyNote = isAbsence
            ? """
              <p style="color:#555;font-size:14px;margin:0 0 16px">
                This is the daily 6:00 PM absence notification from Medical College Attendance.
              </p>
              """
            : "";

        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#1a1a1a;max-width:560px">
              <h2 style="margin:0 0 12px;color:#0f4c81">{WebUtility.HtmlEncode(heading)}</h2>
              <p>{WebUtility.HtmlEncode(greeting)},</p>
              <p>{WebUtility.HtmlEncode(message)}</p>
              <table style="border-collapse:collapse;margin:16px 0;width:100%;background:#f6f8fb;border-radius:8px">
                <tr>
                  <td style="padding:12px 16px;color:#555;width:40%">Student</td>
                  <td style="padding:12px 16px"><strong>{WebUtility.HtmlEncode(studentName)}</strong></td>
                </tr>
                <tr>
                  <td style="padding:12px 16px;color:#555;border-top:1px solid #e5eaf0">Student ID</td>
                  <td style="padding:12px 16px;border-top:1px solid #e5eaf0"><strong>{WebUtility.HtmlEncode(studentId)}</strong></td>
                </tr>
                <tr>
                  <td style="padding:12px 16px;color:#555;border-top:1px solid #e5eaf0">Class</td>
                  <td style="padding:12px 16px;border-top:1px solid #e5eaf0"><strong>{WebUtility.HtmlEncode(className)}</strong></td>
                </tr>
                <tr>
                  <td style="padding:12px 16px;color:#555;border-top:1px solid #e5eaf0">Date</td>
                  <td style="padding:12px 16px;border-top:1px solid #e5eaf0"><strong>{alertDate:dd MMM yyyy}</strong></td>
                </tr>
                <tr>
                  <td style="padding:12px 16px;color:#555;border-top:1px solid #e5eaf0">Status</td>
                  <td style="padding:12px 16px;border-top:1px solid #e5eaf0">
                    <strong style="color:{(isAbsence ? "#d64545" : "#0f4c81")}">{WebUtility.HtmlEncode(statusText)}</strong>
                  </td>
                </tr>
              </table>
              {dailyNote}
              <p style="color:#666;font-size:13px;margin-top:24px">
                If you no longer wish to receive these alerts,
                <a href="{WebUtility.HtmlEncode(unsubscribeUrl)}" style="color:#0f4c81">unsubscribe here</a>.
              </p>
              <p style="color:#999;font-size:12px;margin-top:8px">
                Medical College Attendance · Parent alerts
              </p>
            </div>
            """;

        return SendAsync(toGuardianEmail, subject, html, cancellationToken);
    }

    public async Task<(bool Sent, string Detail)> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default,
        string? fromEmail = null,
        string? fromName = null,
        string? replyToEmail = null)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            return (false, "No recipient email.");

        var enabled = string.Equals(_config["Smtp:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
        var host = (_config["Smtp:Host"] ?? "").Trim();
        var username = (_config["Smtp:Username"] ?? "").Trim();
        var password = (_config["Smtp:Password"] ?? "").Trim();
        var configFrom = (_config["Smtp:FromEmail"] ?? "").Trim();
        var configFromName = (_config["Smtp:FromName"] ?? "Medical College Attendance").Trim();
        var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;

        if (!enabled)
            return (false, "SMTP is disabled (Smtp:Enabled=false).");

        // Prefer creating-admin address as From when it matches the SMTP mailbox;
        // otherwise send via Smtp:Username and Reply-To the admin (Gmail requirement).
        var smtpMailbox = FirstRealEmail(username, configFrom);
        var adminFrom = FirstRealEmail(fromEmail);
        var senderEmail = !string.IsNullOrWhiteSpace(adminFrom) &&
                          adminFrom.Equals(smtpMailbox, StringComparison.OrdinalIgnoreCase)
            ? adminFrom
            : FirstRealEmail(smtpMailbox, adminFrom);
        var senderName = string.IsNullOrWhiteSpace(fromName) ? configFromName : fromName.Trim();

        if (IsPlaceholder(username))
            username = senderEmail;

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            IsPlaceholder(password))
        {
            _logger.LogWarning("SMTP credentials missing. Cannot email {To}.", toEmail);
            return (false, "Set Smtp:Username (admin Gmail) and Smtp:Password (App Password) in appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(senderEmail) || IsPlaceholder(senderEmail))
            senderEmail = username;

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail.Trim()));
            if (!string.IsNullOrWhiteSpace(replyToEmail) && !IsPlaceholder(replyToEmail))
                message.ReplyTo.Add(new MailboxAddress(senderName, replyToEmail.Trim()));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            // Some Windows/corporate networks fail CRL checks and block Gmail TLS.
            client.CheckCertificateRevocation = false;
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(username, password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Sent email From {From} To {To}: {Subject}", senderEmail, toEmail, subject);
            return (true, $"Mail sent from {senderEmail} to {toEmail}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", toEmail);
            return (false, $"Mail failed: {ex.Message}");
        }
    }

    private static string FirstRealEmail(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v) && !IsPlaceholder(v) && v.Contains('@'))
                return v.Trim();
        }
        return string.Empty;
    }

    private static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();
        return v.Contains("PUT_YOUR", StringComparison.OrdinalIgnoreCase)
               || v.Contains("your-email", StringComparison.OrdinalIgnoreCase)
               || v.Contains("your-app-password", StringComparison.OrdinalIgnoreCase)
               || v.Contains("xxxx", StringComparison.OrdinalIgnoreCase);
    }
}
