using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Domain.Interfaces;

public interface IStudentRepository : IRepository<Student>
{
    Task<Student?> GetByUserIdAsync(string userId);
    Task<Student?> GetByStudentIdAsync(string studentId);
    Task<Student?> GetByEnrollmentAsync(string enrollmentNumber);
}
