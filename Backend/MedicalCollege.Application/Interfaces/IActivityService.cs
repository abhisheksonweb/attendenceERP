using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Application.Interfaces;

public interface IActivityService
{
    Task LogAsync(string actorUserId, string actorName, string action, string description, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityItemViewModel>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityItemViewModel>> GetForUserAsync(string userId, int count = 100, CancellationToken cancellationToken = default);
}
