using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Infrastructure.Persistence;

namespace MedicalCollege.Infrastructure.Repositories;

public class ParentAlertRepository : JsonRepositoryBase<ParentAlert>, IParentAlertRepository
{
    public ParentAlertRepository(JsonFileStore store) : base(store, "parent_alerts.json", a => a.Id)
    {
    }

    public async Task<IReadOnlyList<ParentAlert>> GetByStudentAsync(string studentId)
    {
        var all = await GetAllAsync();
        return all.Where(a => a.StudentId == studentId).OrderByDescending(a => a.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<ParentAlert>> GetRecentAsync(int take = 100)
    {
        var all = await GetAllAsync();
        return all.OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToList();
    }
}
