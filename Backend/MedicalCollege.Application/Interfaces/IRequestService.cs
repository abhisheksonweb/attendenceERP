using MedicalCollege.Application.Common;
using MedicalCollege.Application.ViewModels;

namespace MedicalCollege.Application.Interfaces;

public interface IRequestService
{
    Task<ServiceResult<ReviewRequestViewModel>> CreateRequestAsync(
        string studentUserId,
        ProtectedFieldRequestViewModel model,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ReviewRequestViewModel>> CreateAttendanceCorrectionAsync(
        string studentUserId,
        AttendanceCorrectionRequestViewModel model,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewRequestViewModel>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewRequestViewModel>> GetHistoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewRequestViewModel>> GetByStudentAsync(string studentId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ReviewRequestViewModel>> ApproveAsync(
        string id,
        string? remarks,
        string reviewedBy,
        string reviewerName,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<ReviewRequestViewModel>> RejectAsync(
        string id,
        string? remarks,
        string reviewedBy,
        string reviewerName,
        CancellationToken cancellationToken = default);
}
