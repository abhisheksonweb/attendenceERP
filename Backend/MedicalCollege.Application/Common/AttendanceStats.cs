namespace MedicalCollege.Application.Common;

public class AttendanceStats
{
    public int PresentCount { get; init; }
    public int AbsentCount { get; init; }
    public int TotalCount => PresentCount + AbsentCount;
    public double Percentage { get; init; }
}
