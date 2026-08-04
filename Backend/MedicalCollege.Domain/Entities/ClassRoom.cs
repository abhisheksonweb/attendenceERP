namespace MedicalCollege.Domain.Entities;

public class ClassRoom
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Course { get; set; } = string.Empty;
    public string Semester { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Maximum class length in minutes (e.g. 60).</summary>
    public int? MaxClassDurationMinutes { get; set; }
    /// <summary>
    /// Minimum minutes a student must attend. Below this = Partially Present (not Present).
    /// If unset, defaults to 50% of max class duration.
    /// </summary>
    public int? MinAttendanceMinutes { get; set; }
    public string? AdminUserId { get; set; }
    public int? FrmClassId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
