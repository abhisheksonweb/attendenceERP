using MedicalCollege.Application.Interfaces;
using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Infrastructure.Email;
using MedicalCollege.Infrastructure.Erp;
using MedicalCollege.Infrastructure.Frm;
using MedicalCollege.Infrastructure.Persistence;
using MedicalCollege.Infrastructure.Repositories;
using MedicalCollege.Infrastructure.Security;
using MedicalCollege.Infrastructure.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalCollege.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string appDataPath, string? webRootPath = null)
    {
        Directory.CreateDirectory(appDataPath);

        services.AddSingleton(new JsonFileStore(appDataPath));
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
        services.AddScoped<IParentAlertRepository, ParentAlertRepository>();
        services.AddScoped<DataSeeder>();
        services.AddHttpClient<IFrmClient, FrmClient>();
        services.AddHttpClient<IErpIntegrationService, ErpIntegrationService>();
        services.AddHostedService<FrmAttendanceSyncWorker>();
        services.AddHostedService<AbsenceAlertWorker>();

        return services;
    }
}
