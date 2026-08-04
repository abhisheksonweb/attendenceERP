namespace MedicalCollege.Domain.Entities;

public class ActivityLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ActorUserId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
