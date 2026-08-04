using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Infrastructure.Persistence;

namespace MedicalCollege.Infrastructure.Repositories;

public class ClassRepository : JsonRepositoryBase<ClassRoom>, IClassRepository
{
    public ClassRepository(JsonFileStore store) : base(store, "classes.json", c => c.Id) { }

    public async Task<ClassRoom?> GetByCodeAsync(string code)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<ClassRoom>> GetActiveAsync()
    {
        var all = await GetAllAsync();
        return all.Where(c => c.IsActive).OrderBy(c => c.Name).ToList();
    }
}
