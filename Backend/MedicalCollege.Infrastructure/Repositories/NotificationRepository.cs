using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Infrastructure.Persistence;

namespace MedicalCollege.Infrastructure.Repositories;

public class NotificationRepository : JsonRepositoryBase<AppNotification>, INotificationRepository
{
    public NotificationRepository(JsonFileStore store) : base(store, "notifications.json", n => n.Id) { }

    public async Task<IReadOnlyList<AppNotification>> GetByUserAsync(string userId)
    {
        var all = await GetAllAsync();
        return all.Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).ToList();
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        var all = await GetByUserAsync(userId);
        return all.Count(n => !n.IsRead);
    }
}
