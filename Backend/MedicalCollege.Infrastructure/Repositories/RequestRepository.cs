using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Infrastructure.Persistence;

namespace MedicalCollege.Infrastructure.Repositories;

public class RequestRepository : JsonRepositoryBase<ProfileUpdateRequest>, IRequestRepository
{
    public RequestRepository(JsonFileStore store) : base(store, "requests.json", r => r.Id) { }

    public async Task<IReadOnlyList<ProfileUpdateRequest>> GetByStatusAsync(RequestStatus status)
    {
        var all = await GetAllAsync();
        return all.Where(r => r.Status == status).OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<ProfileUpdateRequest>> GetByStudentAsync(string studentId)
    {
        var all = await GetAllAsync();
        return all.Where(r => r.StudentId == studentId).OrderByDescending(r => r.CreatedAt).ToList();
    }
}
