using MedicalCollege.Application.Common;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Enums;

namespace MedicalCollege.Application.Interfaces;

public interface IAttendanceService
{
    Task<IReadOnlyList<AttendanceRecordViewModel>> GetDailyAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceRecordViewModel>> GetMonthlyAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceRecordViewModel>> GetByStudentAsync(string studentId, CancellationToken cancellationToken = default);
    Task<AttendanceStats> GetStatsAsync(string? studentId = null, CancellationToken cancellationToken = default);
    Task<AttendanceStats> GetDailyStatsAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<AttendanceStats> GetMonthlyStatsAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<ServiceResult<AttendanceRecordViewModel>> MarkAttendanceAsync(
        string studentId,
        DateTime date,
        AttendanceStatus status,
        string markedBy,
        string? remarks = null,
        CancellationToken cancellationToken = default);
}
