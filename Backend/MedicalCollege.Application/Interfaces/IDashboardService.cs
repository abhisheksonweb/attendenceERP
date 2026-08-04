using MedicalCollege.Application.ViewModels;

namespace MedicalCollege.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardStatsViewModel> GetAdminDashboardAsync(string userId, CancellationToken cancellationToken = default);
    Task<DashboardStatsViewModel> GetSuperAdminDashboardAsync(CancellationToken cancellationToken = default);
    Task<StudentDashboardViewModel> GetStudentDashboardAsync(string userId, CancellationToken cancellationToken = default);
}
