using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Domain.Interfaces;

public interface IParentAlertRepository : IRepository<ParentAlert>
{
    Task<IReadOnlyList<ParentAlert>> GetByStudentAsync(string studentId);
    Task<IReadOnlyList<ParentAlert>> GetRecentAsync(int take = 100);
}
