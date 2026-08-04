using MedicalCollege.Domain.Enums;

namespace MedicalCollege.Domain.Entities;

public class Student
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string? ClassId { get; set; }
    public int? FrmStudentId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string EnrollmentNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Course { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Semester { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public string? EmergencyContact { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianPhone { get; set; }
    public string? GuardianEmail { get; set; }
    /// <summary>When true, parent alert emails are not sent.</summary>
    public bool ParentEmailUnsubscribed { get; set; }
    public bool IsActive { get; set; } = true;
    public bool FaceRegistered { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
