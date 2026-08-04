using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Domain.Interfaces;

public interface INotificationRepository : IRepository<AppNotification>
{
    Task<IReadOnlyList<AppNotification>> GetByUserAsync(string userId);
    Task<int> GetUnreadCountAsync(string userId);
}
