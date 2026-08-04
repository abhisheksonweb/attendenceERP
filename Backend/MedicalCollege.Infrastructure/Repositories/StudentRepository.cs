using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Infrastructure.Persistence;

namespace MedicalCollege.Infrastructure.Repositories;

public class StudentRepository : JsonRepositoryBase<Student>, IStudentRepository
{
    public StudentRepository(JsonFileStore store) : base(store, "students.json", s => s.Id) { }

    public async Task<Student?> GetByUserIdAsync(string userId)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(s => s.UserId == userId);
    }

    public async Task<Student?> GetByStudentIdAsync(string studentId)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(s => s.StudentId.Equals(studentId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Student?> GetByEnrollmentAsync(string enrollmentNumber)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(s => s.EnrollmentNumber.Equals(enrollmentNumber, StringComparison.OrdinalIgnoreCase));
    }
}
