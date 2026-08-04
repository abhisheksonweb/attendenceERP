using System.ComponentModel.DataAnnotations;

namespace MedicalCollege.Application.ViewModels;

public class ClassFormViewModel
{
    public string? Id { get; set; }

    [Required, Display(Name = "Class Name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public string Department { get; set; } = string.Empty;

    [Required]
    public string Course { get; set; } = string.Empty;

    [Required]
    public string Semester { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Display(Name = "Maximum Class Duration (minutes)")]
    [Range(1, 24 * 60, ErrorMessage = "Enter duration between 1 and 1440 minutes.")]
    public int? MaxClassDurationMinutes { get; set; }

    [Display(Name = "Minimum Attendance (minutes)")]
    [Range(1, 24 * 60, ErrorMessage = "Enter duration between 1 and 1440 minutes.")]
    public int? MinAttendanceMinutes { get; set; }

    public bool IsActive { get; set; } = true;
    public int? FrmClassId { get; set; }
    public int StudentCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ClassDetailViewModel
{
    public ClassFormViewModel Class { get; set; } = new();
    public IReadOnlyList<StudentFormViewModel> Students { get; set; } = Array.Empty<StudentFormViewModel>();
    public IReadOnlyList<AttendanceRecordViewModel> TodayAttendance { get; set; } = Array.Empty<AttendanceRecordViewModel>();
    public string? FaceRecognizeUrl { get; set; }
    public bool FrmSynced { get; set; }
}
