using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Infrastructure.Persistence;

namespace MedicalCollege.Infrastructure.Repositories;

public class ActivityLogRepository : JsonRepositoryBase<ActivityLog>, IActivityLogRepository
{
    public ActivityLogRepository(JsonFileStore store) : base(store, "activities.json", a => a.Id) { }

    public async Task<IReadOnlyList<ActivityLog>> GetRecentAsync(int count = 20)
    {
        var all = await GetAllAsync();
        return all.OrderByDescending(a => a.CreatedAt).Take(count).ToList();
    }

    public async Task<IReadOnlyList<ActivityLog>> GetByActorAsync(string actorUserId, int count = 100)
    {
        var all = await GetAllAsync();
        return all
            .Where(a => a.ActorUserId.Equals(actorUserId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.CreatedAt)
            .Take(count)
            .ToList();
    }
}
