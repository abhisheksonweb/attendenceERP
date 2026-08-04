using MedicalCollege.Application.Common;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;

namespace MedicalCollege.Application.Services;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepository;

    public NotificationService(INotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public async Task<IReadOnlyList<NotificationViewModel>> GetForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.GetByUserAsync(userId);
        return notifications
            .OrderByDescending(n => n.CreatedAt)
            .Select(MapToViewModel)
            .ToList();
    }

    public Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default) =>
        _notificationRepository.GetUnreadCountAsync(userId);

    public async Task MarkReadAsync(string notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification is null || notification.IsRead)
            return;

        notification.IsRead = true;
        await _notificationRepository.UpdateAsync(notification);
    }

    public async Task MarkAllReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.GetByUserAsync(userId);
        var unread = notifications.Where(n => !n.IsRead).ToList();
        if (unread.Count == 0)
            return;

        foreach (var notification in unread)
            notification.IsRead = true;

        await _notificationRepository.SaveAllAsync(unread);
    }

    public async Task<ServiceResult<NotificationViewModel>> CreateAsync(
        string userId,
        string title,
        string message,
        NotificationType type,
        string? linkUrl = null,
        CancellationToken cancellationToken = default,
        string? classId = null,
        string? className = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceResult<NotificationViewModel>.Fail("User id is required.");

        var notification = new AppNotification
        {
            UserId = userId,
            Title = title.Trim(),
            Message = message.Trim(),
            Type = type,
            LinkUrl = linkUrl,
            ClassId = string.IsNullOrWhiteSpace(classId) ? null : classId.Trim(),
            ClassName = string.IsNullOrWhiteSpace(className) ? null : className.Trim(),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        await _notificationRepository.AddAsync(notification);
        return ServiceResult<NotificationViewModel>.Ok(MapToViewModel(notification), "Notification created.");
    }

    private static NotificationViewModel MapToViewModel(AppNotification notification) => new()
    {
        Id = notification.Id,
        Title = notification.Title,
        Message = notification.Message,
        Type = notification.Type.ToString(),
        IsRead = notification.IsRead,
        LinkUrl = notification.LinkUrl,
        ClassId = notification.ClassId,
        ClassName = notification.ClassName,
        CreatedAt = notification.CreatedAt
    };
}
