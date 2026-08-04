using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;

namespace MedicalCollege.Domain.Interfaces;

public interface IRequestRepository : IRepository<ProfileUpdateRequest>
{
    Task<IReadOnlyList<ProfileUpdateRequest>> GetByStatusAsync(RequestStatus status);
    Task<IReadOnlyList<ProfileUpdateRequest>> GetByStudentAsync(string studentId);
}
