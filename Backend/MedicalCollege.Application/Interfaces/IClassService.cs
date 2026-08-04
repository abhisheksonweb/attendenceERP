using MedicalCollege.Application.Common;
using MedicalCollege.Application.ViewModels;

namespace MedicalCollege.Application.Interfaces;

public interface IClassService
{
    Task<IReadOnlyList<ClassFormViewModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ClassFormViewModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<ClassDetailViewModel?> GetDetailAsync(string id, CancellationToken cancellationToken = default);
    Task<ServiceResult<ClassFormViewModel>> CreateAsync(ClassFormViewModel model, string actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<ServiceResult<ClassFormViewModel>> UpdateAsync(ClassFormViewModel model, string actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<ServiceResult> DeactivateAsync(string id, string actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task SetFrmClassIdAsync(string classId, int frmClassId, CancellationToken cancellationToken = default);
}
