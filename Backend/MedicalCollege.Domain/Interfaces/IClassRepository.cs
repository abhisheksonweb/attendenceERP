using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Domain.Interfaces;

public interface IClassRepository : IRepository<ClassRoom>
{
    Task<ClassRoom?> GetByCodeAsync(string code);
    Task<IReadOnlyList<ClassRoom>> GetActiveAsync();
}
