using MedicalCollege.Application.Common;
using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Application.Interfaces;

public interface IParentNotificationService
{
    Task<ServiceResult> NotifyAbsenceAsync(Student student, string className, DateTime date, CancellationToken ct = default);
    Task<ServiceResult> NotifyEarlyLeaveAsync(Student student, string className, string? outTime, CancellationToken ct = default);
    Task<ServiceResult> NotifyAttendanceUpdateAsync(
        Student student, string className, string status, string? inTime, string? outTime, CancellationToken ct = default);
    Task<IReadOnlyList<ParentAlert>> GetRecentAsync(int take = 100, CancellationToken ct = default);
    Task<ServiceResult> UnsubscribeParentEmailAsync(string studentId, CancellationToken ct = default);
}
