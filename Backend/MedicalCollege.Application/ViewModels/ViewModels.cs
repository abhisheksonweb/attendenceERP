using System.ComponentModel.DataAnnotations;
using MedicalCollege.Domain.Enums;

namespace MedicalCollege.Application.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress, Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember Me")]
    public bool RememberMe { get; set; }

    public UserRole? PreferredRole { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class AdminFormViewModel
{
    public string? Id { get; set; }

    [Required, Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Username { get; set; } = string.Empty;

    [DataType(DataType.Password), MinLength(6)]
    public string? Password { get; set; }

    public bool IsActive { get; set; } = true;
}

public class StudentFormViewModel
{
    public string? Id { get; set; }

    [Display(Name = "Student ID")]
    public string StudentId { get; set; } = string.Empty;

    [Display(Name = "Enrollment Number")]
    public string EnrollmentNumber { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string Course { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public string Semester { get; set; } = string.Empty;

    [Required, Display(Name = "Class")]
    public string? ClassId { get; set; } = string.Empty;

    [Required, Phone, Display(Name = "Mobile")]
    public string Mobile { get; set; } = string.Empty;

    [Required, DataType(DataType.Date), Display(Name = "Date of Birth")]
    public DateTime DateOfBirth { get; set; } = DateTime.Today.AddYears(-18);

    [Required]
    public Gender Gender { get; set; } = Gender.Male;

    public string Username { get; set; } = string.Empty;

    [Display(Name = "Guardian / Parent Name")]
    public string? GuardianName { get; set; }

    [Phone, Display(Name = "Guardian Phone")]
    public string? GuardianPhone { get; set; }

    [EmailAddress, Display(Name = "Guardian Email")]
    public string? GuardianEmail { get; set; }

    [DataType(DataType.Password), Display(Name = "Temporary Password")]
    public string? TemporaryPassword { get; set; }

    /// <summary>When true, student must change password before accessing the portal.</summary>
    [Display(Name = "Force password change on first login")]
    public bool ForcePasswordChange { get; set; } = true;

    public string? ProfilePhotoPath { get; set; }
    public bool IsActive { get; set; } = true;
    public bool FaceRegistered { get; set; }
    public int? FrmStudentId { get; set; }
}

public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current Password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(6), Display(Name = "New Password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Display(Name = "Confirm New Password"), Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class StudentProfileViewModel
{
    public string Id { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public string EnrollmentNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Course { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Semester { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string? ProfilePhotoPath { get; set; }

    [Phone, Display(Name = "Phone Number")]
    public string Mobile { get; set; } = string.Empty;

    [Display(Name = "Emergency Contact")]
    public string? EmergencyContact { get; set; }

    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }

    [Display(Name = "Guardian Name")]
    public string? GuardianName { get; set; }

    [Display(Name = "Guardian Phone")]
    public string? GuardianPhone { get; set; }

    [EmailAddress, Display(Name = "Guardian Email")]
    public string? GuardianEmail { get; set; }

    public int ProfileCompletionPercent { get; set; }
    public bool FaceRegistered { get; set; }
}

public class ProtectedFieldRequestViewModel
{
    [Required]
    public string FieldName { get; set; } = string.Empty;

    [Required, Display(Name = "Requested Value")]
    public string NewValue { get; set; } = string.Empty;
}

public class AttendanceCorrectionRequestViewModel
{
    [Required]
    public DateTime Date { get; set; }

    /// <summary>AbsentCorrection (mark Present) or TimingCorrection (fix IN/OUT).</summary>
    public string RequestKind { get; set; } = "AbsentCorrection";

    [Display(Name = "Requested In Time")]
    public string? RequestedInTime { get; set; }

    [Display(Name = "Requested Out Time")]
    public string? RequestedOutTime { get; set; }

    [Required, Display(Name = "Reason"), StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public class ReviewRequestViewModel
{
    public string Id { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AdminRemarks { get; set; }
}

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class StudentListFilter
{
    public string? Search { get; set; }
    public string? Department { get; set; }
    public string? Course { get; set; }
    public string? Semester { get; set; }
    public bool? IsActive { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class DashboardStatsViewModel
{
    public int TotalStudents { get; set; }
    public int PresentToday { get; set; }
    public int AbsentToday { get; set; }
    public int PendingFaceRegistration { get; set; }
    public int PendingProfileRequests { get; set; }
    public int UnreadNotifications { get; set; }
    public double AttendancePercentage { get; set; }
    public int UnknownFaces { get; set; }
    public int TotalAdmins { get; set; }
    public int ActiveAdmins { get; set; }
    public IReadOnlyList<ChartPoint> WeeklyAttendance { get; set; } = Array.Empty<ChartPoint>();
    public IReadOnlyList<ActivityItemViewModel> RecentActivities { get; set; } = Array.Empty<ActivityItemViewModel>();
}

public class ChartPoint
{
    public string Label { get; set; } = string.Empty;
    public double Value { get; set; }
}

public class ActivityItemViewModel
{
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class StudentDashboardViewModel
{
    public string Name { get; set; } = string.Empty;
    public double AttendancePercentage { get; set; }
    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public int TotalDays { get; set; }
    public int ProfileCompletionPercent { get; set; }
    public bool FaceRegistered { get; set; }
    public int UnreadNotifications { get; set; }
    public IReadOnlyList<AttendanceRecordViewModel> RecentAttendance { get; set; } = Array.Empty<AttendanceRecordViewModel>();
}

public class AttendanceRecordViewModel
{
    public string Id { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Course { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string? FirstIn { get; set; }
    public string? LastOut { get; set; }
    public string? Duration { get; set; }
    public int? DurationSeconds { get; set; }
    public bool EarlyLeave { get; set; }
}

public class NotificationViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string? LinkUrl { get; set; }
    public string? ClassId { get; set; }
    public string? ClassName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ComingSoonViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-hourglass-split";
}

public class StudentImportRowResult
{
    public int RowNumber { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? ClassCode { get; set; }
    public string? PortalStudentId { get; set; }
    public string? ClassId { get; set; }
    public string? PhotoUrl { get; set; }
    public bool FaceEnrolled { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class StudentImportResultViewModel
{
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    /// <summary>True when validation found errors and no students were added.</summary>
    public bool AbortedDueToErrors { get; set; }
    public IReadOnlyList<string> AffectedClassIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<StudentImportRowResult> Rows { get; set; } = Array.Empty<StudentImportRowResult>();
}
