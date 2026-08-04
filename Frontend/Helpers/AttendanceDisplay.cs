namespace MedicalCollege.Web.Helpers;

/// <summary>Maps stored attendance status codes to end-user labels.</summary>
public static class AttendanceDisplay
{
    public static string StatusLabel(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "—";
        if (status.Equals("PartialAbsent", StringComparison.OrdinalIgnoreCase))
            return "Partially Present";
        if (status.Equals("WeekOff", StringComparison.OrdinalIgnoreCase))
            return "Week Off";
        return status;
    }

    public static bool IsFullyPresent(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        (status.Equals("Present", StringComparison.OrdinalIgnoreCase)
         || status.Equals("Late", StringComparison.OrdinalIgnoreCase));

    public static bool IsPartiallyPresent(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        status.Equals("PartialAbsent", StringComparison.OrdinalIgnoreCase);

    public static string BadgeClass(string? status)
    {
        if (IsFullyPresent(status)) return "text-bg-success";
        if (IsPartiallyPresent(status)) return "text-bg-warning";
        if (!string.IsNullOrWhiteSpace(status) &&
            status.Equals("WeekOff", StringComparison.OrdinalIgnoreCase))
            return "text-bg-secondary";
        return "text-bg-danger";
    }
}
