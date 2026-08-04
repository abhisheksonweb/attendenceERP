namespace MedicalCollege.Application.Interfaces;

public interface IEmailService
{
    /// <summary>Sends an email. Returns true when delivered (or queued); false when skipped/failed.</summary>
    Task<(bool Sent, string Detail)> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default,
        string? fromEmail = null,
        string? fromName = null,
        string? replyToEmail = null);

    /// <summary>Welcome mail: From = creating admin, To = new student.</summary>
    Task<(bool Sent, string Detail)> SendStudentWelcomeAsync(
        string toStudentEmail,
        string studentName,
        string studentId,
        string temporaryPassword,
        string fromAdminEmail,
        string fromAdminName,
        CancellationToken cancellationToken = default);

    Task<(bool Sent, string Detail)> SendPasswordResetAsync(
        string toEmail,
        string studentName,
        string temporaryPassword,
        string? fromAdminEmail = null,
        string? fromAdminName = null,
        CancellationToken cancellationToken = default);
}
