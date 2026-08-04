using MedicalCollege.Application.Common;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Application.Interfaces;

public interface IAuthService
{
    Task<User?> ValidateLoginAsync(string username, string password, CancellationToken cancellationToken = default);

    Task<ServiceResult> ChangePasswordAsync(
        string userId,
        ChangePasswordViewModel model,
        CancellationToken cancellationToken = default);
}
