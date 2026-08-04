using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Infrastructure.Persistence;

namespace MedicalCollege.Infrastructure.Repositories;

public class AttendanceRepository : JsonRepositoryBase<AttendanceRecord>, IAttendanceRepository
{
    public AttendanceRepository(JsonFileStore store) : base(store, "attendance.json", a => a.Id) { }

    public async Task<IReadOnlyList<AttendanceRecord>> GetByDateAsync(DateTime date)
    {
        var all = await GetAllAsync();
        return all.Where(a => a.Date.Date == date.Date).ToList();
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetByStudentAsync(string studentId)
    {
        var all = await GetAllAsync();
        return all.Where(a => a.StudentId == studentId).OrderByDescending(a => a.Date).ToList();
    }

    public async Task<IReadOnlyList<AttendanceRecord>> GetByMonthAsync(int year, int month)
    {
        var all = await GetAllAsync();
        return all.Where(a => a.Date.Year == year && a.Date.Month == month).ToList();
    }
}
