using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Interfaces;

namespace MedicalCollege.Application.Services;

public class ActivityService : IActivityService
{
    private readonly IActivityLogRepository _activityLogRepository;

    public ActivityService(IActivityLogRepository activityLogRepository)
    {
        _activityLogRepository = activityLogRepository;
    }

    public async Task LogAsync(
        string actorUserId,
        string actorName,
        string action,
        string description,
        CancellationToken cancellationToken = default)
    {
        var log = new ActivityLog
        {
            ActorUserId = actorUserId,
            ActorName = actorName,
            Action = action,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        await _activityLogRepository.AddAsync(log);
    }

    public async Task<IReadOnlyList<ActivityItemViewModel>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        var logs = await _activityLogRepository.GetRecentAsync(count);
        return logs.Select(MapToViewModel).ToList();
    }

    public async Task<IReadOnlyList<ActivityItemViewModel>> GetForUserAsync(string userId, int count = 100, CancellationToken cancellationToken = default)
    {
        var logs = await _activityLogRepository.GetByActorAsync(userId, count);
        return logs.Select(MapToViewModel).ToList();
    }

    private static ActivityItemViewModel MapToViewModel(ActivityLog log) => new()
    {
        ActorName = log.ActorName,
        Action = log.Action,
        Description = log.Description,
        CreatedAt = log.CreatedAt
    };
}
