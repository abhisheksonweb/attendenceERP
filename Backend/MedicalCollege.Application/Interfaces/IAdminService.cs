using MedicalCollege.Application.Common;
using MedicalCollege.Application.ViewModels;

namespace MedicalCollege.Application.Interfaces;

public interface IAdminService
{
    Task<IReadOnlyList<AdminFormViewModel>> GetAdminsAsync(CancellationToken cancellationToken = default);
    Task<AdminFormViewModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<ServiceResult<AdminFormViewModel>> CreateAdminAsync(
        AdminFormViewModel model,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<AdminFormViewModel>> UpdateAdminAsync(
        AdminFormViewModel model,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default);
    Task<ServiceResult> DisableAdminAsync(
        string id,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<string>> ResetPasswordAsync(
        string id,
        string newPassword,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default);
}
