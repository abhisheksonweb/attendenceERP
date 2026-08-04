using MedicalCollege.Application.Common;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;

namespace MedicalCollege.Application.Services;

public class AdminService : IAdminService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IActivityService _activityService;
    private readonly INotificationService _notificationService;

    public AdminService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IActivityService activityService,
        INotificationService notificationService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _activityService = activityService;
        _notificationService = notificationService;
    }

    public async Task<IReadOnlyList<AdminFormViewModel>> GetAdminsAsync(CancellationToken cancellationToken = default)
    {
        var admins = await _userRepository.GetByRoleAsync(UserRole.Admin);
        return admins
            .OrderBy(a => a.FullName)
            .Select(MapToViewModel)
            .ToList();
    }

    public async Task<AdminFormViewModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user is null || user.Role != UserRole.Admin)
            return null;

        return MapToViewModel(user);
    }

    public async Task<ServiceResult<AdminFormViewModel>> CreateAdminAsync(
        AdminFormViewModel model,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAdminFormAsync(model, isCreate: true, cancellationToken);
        if (!validation.Success)
            return ServiceResult<AdminFormViewModel>.Fail(validation.Message!);

        var user = new User
        {
            Username = model.Username.Trim(),
            Email = model.Email.Trim(),
            FullName = model.FullName.Trim(),
            PasswordHash = _passwordHasher.Hash(model.Password!),
            Role = UserRole.Admin,
            IsActive = model.IsActive,
            MustChangePassword = true,
            CreatedBy = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);

        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "Admin Created",
            $"Created admin account for {user.FullName} ({user.Username}).");

        return ServiceResult<AdminFormViewModel>.Ok(MapToViewModel(user), "Admin created successfully.");
    }

    public async Task<ServiceResult<AdminFormViewModel>> UpdateAdminAsync(
        AdminFormViewModel model,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
            return ServiceResult<AdminFormViewModel>.Fail("Admin id is required.");

        var user = await _userRepository.GetByIdAsync(model.Id);
        if (user is null || user.Role != UserRole.Admin)
            return ServiceResult<AdminFormViewModel>.Fail("Admin not found.");

        var validation = await ValidateAdminFormAsync(model, isCreate: false, cancellationToken);
        if (!validation.Success)
            return ServiceResult<AdminFormViewModel>.Fail(validation.Message!);

        user.FullName = model.FullName.Trim();
        user.Email = model.Email.Trim();
        user.Username = model.Username.Trim();
        user.IsActive = model.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            user.PasswordHash = _passwordHasher.Hash(model.Password);
            user.MustChangePassword = true;
        }

        await _userRepository.UpdateAsync(user);

        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "Admin Updated",
            $"Updated admin account for {user.FullName} ({user.Username}).");

        return ServiceResult<AdminFormViewModel>.Ok(MapToViewModel(user), "Admin updated successfully.");
    }

    public async Task<ServiceResult> DisableAdminAsync(
        string id,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user is null || user.Role != UserRole.Admin)
            return ServiceResult.Fail("Admin not found.");

        if (!user.IsActive)
            return ServiceResult.Ok("Admin is already disabled.");

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "Admin Disabled",
            $"Disabled admin account for {user.FullName} ({user.Username}).");

        return ServiceResult.Ok("Admin disabled successfully.");
    }

    public async Task<ServiceResult<string>> ResetPasswordAsync(
        string id,
        string newPassword,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            return ServiceResult<string>.Fail("Password must be at least 6 characters.");

        var user = await _userRepository.GetByIdAsync(id);
        if (user is null || user.Role != UserRole.Admin)
            return ServiceResult<string>.Fail("Admin not found.");

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        user.MustChangePassword = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        await _notificationService.CreateAsync(
            user.Id,
            "Password Reset",
            "Your password has been reset by a super administrator. Please sign in with your new password and change it immediately.",
            NotificationType.PasswordReset,
            "/Account/Login",
            cancellationToken);

        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "Admin Password Reset",
            $"Reset password for admin {user.FullName} ({user.Username}).");

        return ServiceResult<string>.Ok(newPassword, "Password reset successfully.");
    }

    private async Task<ServiceResult> ValidateAdminFormAsync(
        AdminFormViewModel model,
        bool isCreate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.FullName))
            return ServiceResult.Fail("Full name is required.");

        if (string.IsNullOrWhiteSpace(model.Email))
            return ServiceResult.Fail("Email is required.");

        if (string.IsNullOrWhiteSpace(model.Username))
            return ServiceResult.Fail("Username is required.");

        if (isCreate && string.IsNullOrWhiteSpace(model.Password))
            return ServiceResult.Fail("Password is required.");

        if (!string.IsNullOrWhiteSpace(model.Password) && model.Password.Length < 6)
            return ServiceResult.Fail("Password must be at least 6 characters.");

        var existingUsername = await _userRepository.GetByUsernameAsync(model.Username.Trim());
        if (existingUsername is not null && existingUsername.Id != model.Id)
            return ServiceResult.Fail("Username is already in use.");

        var existingEmail = await _userRepository.GetByEmailAsync(model.Email.Trim());
        if (existingEmail is not null && existingEmail.Id != model.Id)
            return ServiceResult.Fail("Email is already in use.");

        return ServiceResult.Ok();
    }

    private static AdminFormViewModel MapToViewModel(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        Username = user.Username,
        IsActive = user.IsActive
    };
}
