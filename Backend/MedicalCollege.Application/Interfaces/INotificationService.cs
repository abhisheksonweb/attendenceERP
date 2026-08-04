using MedicalCollege.Application.Common;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Enums;

namespace MedicalCollege.Application.Interfaces;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationViewModel>> GetForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default);
    Task MarkReadAsync(string notificationId, CancellationToken cancellationToken = default);
    Task MarkAllReadAsync(string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<NotificationViewModel>> CreateAsync(
        string userId,
        string title,
        string message,
        NotificationType type,
        string? linkUrl = null,
        CancellationToken cancellationToken = default,
        string? classId = null,
        string? className = null);
}
