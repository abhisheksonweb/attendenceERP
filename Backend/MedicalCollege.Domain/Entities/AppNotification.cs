using MedicalCollege.Domain.Enums;

namespace MedicalCollege.Domain.Entities;

public class AppNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; } = NotificationType.General;
    public bool IsRead { get; set; }
    public string? LinkUrl { get; set; }
    /// <summary>When set, admin inbox can filter this alert by class.</summary>
    public string? ClassId { get; set; }
    public string? ClassName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
