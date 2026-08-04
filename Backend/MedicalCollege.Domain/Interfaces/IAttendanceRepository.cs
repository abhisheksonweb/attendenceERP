using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Domain.Interfaces;

public interface IAttendanceRepository : IRepository<AttendanceRecord>
{
    Task<IReadOnlyList<AttendanceRecord>> GetByDateAsync(DateTime date);
    Task<IReadOnlyList<AttendanceRecord>> GetByStudentAsync(string studentId);
    Task<IReadOnlyList<AttendanceRecord>> GetByMonthAsync(int year, int month);
}
