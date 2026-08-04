using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalCollege.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<IParentNotificationService, ParentNotificationService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IClassService, ClassService>();

        return services;
    }
}
