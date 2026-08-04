using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Domain.Interfaces;

public interface IActivityLogRepository : IRepository<ActivityLog>
{
    Task<IReadOnlyList<ActivityLog>> GetRecentAsync(int count = 20);
    Task<IReadOnlyList<ActivityLog>> GetByActorAsync(string actorUserId, int count = 100);
}
