namespace MedicalCollege.Domain.Entities;

/// <summary>
/// On-prem parent alert log (SMS/email channel adapters write here; NMC-friendly local store).
/// </summary>
public class ParentAlert
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string? ClassId { get; set; }
    public string? ClassName { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianPhone { get; set; }
    public string? GuardianEmail { get; set; }
    public string Channel { get; set; } = "Log"; // Log | Sms | Email
    public string AlertType { get; set; } = string.Empty; // Absence | EarlyLeave | CheckedIn | CheckedOut
    public string Message { get; set; } = string.Empty;
    public bool Delivered { get; set; }
    public string? DeliveryNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
