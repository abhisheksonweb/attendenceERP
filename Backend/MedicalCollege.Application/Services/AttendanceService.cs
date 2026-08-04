using MedicalCollege.Application.Common;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;

namespace MedicalCollege.Application.Services;

public class AttendanceService : IAttendanceService
{
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IStudentRepository _studentRepository;

    public AttendanceService(IAttendanceRepository attendanceRepository, IStudentRepository studentRepository)
    {
        _attendanceRepository = attendanceRepository;
        _studentRepository = studentRepository;
    }

    public async Task<IReadOnlyList<AttendanceRecordViewModel>> GetDailyAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var records = await _attendanceRepository.GetByDateAsync(date.Date);
        return records
            .OrderBy(r => r.StudentName)
            .Select(MapToViewModel)
            .ToList();
    }

    public async Task<IReadOnlyList<AttendanceRecordViewModel>> GetMonthlyAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var records = await _attendanceRepository.GetByMonthAsync(year, month);
        return records
            .OrderByDescending(r => r.Date)
            .ThenBy(r => r.StudentName)
            .Select(MapToViewModel)
            .ToList();
    }

    public async Task<IReadOnlyList<AttendanceRecordViewModel>> GetByStudentAsync(string studentId, CancellationToken cancellationToken = default)
    {
        var calendar = await BuildStudentCalendarAsync(studentId, cancellationToken);
        return calendar
            .OrderByDescending(r => r.Date)
            .ToList();
    }

    public async Task<AttendanceStats> GetStatsAsync(string? studentId = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(studentId))
        {
            var calendar = await BuildStudentCalendarAsync(studentId, cancellationToken);
            return CalculateStatsFromViewModels(calendar);
        }

        var records = await _attendanceRepository.GetAllAsync();
        return CalculateStats(records);
    }

    public async Task<AttendanceStats> GetDailyStatsAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var records = await _attendanceRepository.GetByDateAsync(date.Date);
        return CalculateStats(records);
    }

    public async Task<AttendanceStats> GetMonthlyStatsAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var records = await _attendanceRepository.GetByMonthAsync(year, month);
        return CalculateStats(records);
    }

    public async Task<ServiceResult<AttendanceRecordViewModel>> MarkAttendanceAsync(
        string studentId,
        DateTime date,
        AttendanceStatus status,
        string markedBy,
        string? remarks = null,
        CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByIdAsync(studentId);
        if (student is null)
            return ServiceResult<AttendanceRecordViewModel>.Fail("Student not found.");

        var targetDate = date.Date;
        var existing = await _attendanceRepository.GetByDateAsync(targetDate);
        var duplicate = existing.FirstOrDefault(r =>
            r.StudentId == studentId &&
            r.Date.Date == targetDate);

        if (duplicate is not null)
        {
            duplicate.Status = status;
            duplicate.MarkedBy = markedBy;
            duplicate.Remarks = remarks?.Trim();
            duplicate.Source = "Manual";
            await _attendanceRepository.UpdateAsync(duplicate);
            return ServiceResult<AttendanceRecordViewModel>.Ok(MapToViewModel(duplicate), "Attendance updated.");
        }

        var record = new AttendanceRecord
        {
            StudentId = student.Id,
            StudentCode = student.StudentId,
            StudentName = student.Name,
            Department = student.Department,
            Course = student.Course,
            Date = targetDate,
            Status = status,
            MarkedBy = markedBy,
            Remarks = remarks?.Trim(),
            Source = "Manual",
            CreatedAt = DateTime.UtcNow
        };

        await _attendanceRepository.AddAsync(record);
        return ServiceResult<AttendanceRecordViewModel>.Ok(MapToViewModel(record), "Attendance marked.");
    }

    /// <summary>
    /// Builds a day-by-day history from the student's start date through today.
    /// Sundays with no record are Week Off; other missing days are Absent.
    /// </summary>
    private async Task<List<AttendanceRecordViewModel>> BuildStudentCalendarAsync(
        string studentId,
        CancellationToken cancellationToken)
    {
        var student = await _studentRepository.GetByIdAsync(studentId);
        var stored = await _attendanceRepository.GetByStudentAsync(studentId);
        var byDate = stored
            .GroupBy(r => r.Date.Date)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());

        var today = DateTime.Today;
        var start = student?.CreatedAt.ToLocalTime().Date ?? today;
        if (byDate.Count > 0)
        {
            var firstRecord = byDate.Keys.Min();
            if (firstRecord < start)
                start = firstRecord;
        }

        if (start > today)
            start = today;

        var result = new List<AttendanceRecordViewModel>();
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            if (byDate.TryGetValue(day, out var record))
            {
                result.Add(MapToViewModel(record));
                continue;
            }

            var isSunday = day.DayOfWeek == DayOfWeek.Sunday;
            result.Add(new AttendanceRecordViewModel
            {
                Id = isSunday ? $"auto-weekoff-{day:yyyyMMdd}" : $"auto-absent-{day:yyyyMMdd}",
                StudentCode = student?.StudentId ?? "",
                StudentName = student?.Name ?? "",
                Department = student?.Department ?? "",
                Course = student?.Course ?? "",
                Date = day,
                Status = isSunday ? nameof(AttendanceStatus.WeekOff) : nameof(AttendanceStatus.Absent),
                Source = "System",
                Remarks = isSunday ? "Sunday week off." : "No attendance marked for this day."
            });
        }

        return result;
    }

    internal static AttendanceStats CalculateStats(IEnumerable<AttendanceRecord> records)
    {
        var list = records.ToList();
        var present = list.Count(r =>
            r.Status == AttendanceStatus.Present || r.Status == AttendanceStatus.Late);
        var absent = list.Count(r => r.Status == AttendanceStatus.Absent);
        var total = present + absent;

        return new AttendanceStats
        {
            PresentCount = present,
            AbsentCount = absent,
            Percentage = total == 0 ? 0 : Math.Round(present / (double)total * 100, 2)
        };
    }

    internal static AttendanceStats CalculateStatsFromViewModels(IEnumerable<AttendanceRecordViewModel> records)
    {
        var list = records.ToList();
        var present = list.Count(r =>
            r.Status.Equals(nameof(AttendanceStatus.Present), StringComparison.OrdinalIgnoreCase)
            || r.Status.Equals(nameof(AttendanceStatus.Late), StringComparison.OrdinalIgnoreCase));
        var absent = list.Count(r =>
            r.Status.Equals(nameof(AttendanceStatus.Absent), StringComparison.OrdinalIgnoreCase));
        var total = present + absent;

        return new AttendanceStats
        {
            PresentCount = present,
            AbsentCount = absent,
            Percentage = total == 0 ? 0 : Math.Round(present / (double)total * 100, 2)
        };
    }

    private static AttendanceRecordViewModel MapToViewModel(AttendanceRecord record) => new()
    {
        Id = record.Id,
        StudentCode = record.StudentCode,
        StudentName = record.StudentName,
        Department = record.Department,
        Course = record.Course,
        Date = record.Date,
        Status = AttendanceFormatting.ToDisplayStatus(record.Status),
        Source = record.Source,
        Remarks = record.Remarks,
        FirstIn = record.FirstIn,
        LastOut = record.LastOut,
        Duration = record.Duration,
        DurationSeconds = record.DurationSeconds,
        EarlyLeave = record.EarlyLeave
    };
}
