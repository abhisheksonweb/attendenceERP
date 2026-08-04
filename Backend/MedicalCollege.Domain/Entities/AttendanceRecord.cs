using MedicalCollege.Domain.Enums;

namespace MedicalCollege.Domain.Entities;

public class AttendanceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StudentId { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Course { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? MarkedBy { get; set; }
    public string? Remarks { get; set; }
    public string? FirstIn { get; set; }
    public string? LastOut { get; set; }
    /// <summary>Formatted time in class, e.g. "45m" or "1h 10m".</summary>
    public string? Duration { get; set; }
    /// <summary>Total attended seconds from face sessions.</summary>
    public int? DurationSeconds { get; set; }
    /// <summary>True when attended less than class MinAttendanceMinutes (partial absence).</summary>
    public bool EarlyLeave { get; set; }
    public string Source { get; set; } = "Manual";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
