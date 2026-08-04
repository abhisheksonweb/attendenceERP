namespace MedicalCollege.Domain.Enums;

public enum AttendanceStatus
{
    Present = 1,
    Absent = 2,
    Late = 3,
    Excused = 4,
    WeekOff = 5,
    /// <summary>Checked in but attended less than the class minimum duration (shown as Partially Present).</summary>
    PartialAbsent = 6
}
