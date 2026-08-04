using MedicalCollege.Domain.Enums;

namespace MedicalCollege.Application.Common;

/// <summary>Canonical user-facing labels for attendance status codes.</summary>
public static class AttendanceFormatting
{
    public const string PartiallyPresentLabel = "Partially Present";

    public static string ToDisplayStatus(AttendanceStatus status) => status switch
    {
        AttendanceStatus.PartialAbsent => PartiallyPresentLabel,
        AttendanceStatus.WeekOff => "Week Off",
        _ => status.ToString()
    };

    public static bool IsPartiallyPresent(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        (status.Equals(PartiallyPresentLabel, StringComparison.OrdinalIgnoreCase)
         || status.Equals(nameof(AttendanceStatus.PartialAbsent), StringComparison.OrdinalIgnoreCase)
         || status.Equals("PartiallyPresent", StringComparison.OrdinalIgnoreCase));
}
