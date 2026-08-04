using MedicalCollege.Application.Common;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Interfaces;

namespace MedicalCollege.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    public AuthService(IUserRepository userRepository, IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task<User?> ValidateLoginAsync(string usernameOrEmail, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrWhiteSpace(password))
            return null;

        var key = usernameOrEmail.Trim();
        var user = await _userRepository.GetByEmailAsync(key)
                   ?? await _userRepository.GetByUsernameAsync(key);

        if (user is null && key.Contains('@'))
        {
            var all = await _userRepository.GetAllAsync();
            user = all.FirstOrDefault(u =>
                u.Email.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        if (user is null || !user.IsActive)
            return null;

        return _passwordHasher.Verify(password, user.PasswordHash) ? user : null;
    }

    public async Task<ServiceResult> ChangePasswordAsync(
        string userId,
        ChangePasswordViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.NewPassword) || model.NewPassword.Length < 6)
            return ServiceResult.Fail("New password must be at least 6 characters.");

        if (!string.Equals(model.NewPassword, model.ConfirmPassword, StringComparison.Ordinal))
            return ServiceResult.Fail("New password and confirmation do not match.");

        var user = await _userRepository.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
            return ServiceResult.Fail("User not found.");

        if (!_passwordHasher.Verify(model.CurrentPassword, user.PasswordHash))
            return ServiceResult.Fail("Current password is incorrect.");

        user.PasswordHash = _passwordHasher.Hash(model.NewPassword);
        user.MustChangePassword = false;
        await _userRepository.UpdateAsync(user);
        return ServiceResult.Ok("Password updated successfully.");
    }
}
