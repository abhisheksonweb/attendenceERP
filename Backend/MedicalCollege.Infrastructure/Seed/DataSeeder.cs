using MedicalCollege.Application.Interfaces;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Infrastructure.Persistence;

namespace MedicalCollege.Infrastructure.Seed;

public class DataSeeder
{
    private readonly JsonFileStore _store;
    private readonly IPasswordHasher _hasher;
    private readonly IUserRepository _users;
    private readonly IStudentRepository _students;
    private readonly IClassRepository _classes;
    private readonly IAttendanceRepository _attendance;
    private readonly INotificationRepository _notifications;
    private readonly IActivityLogRepository _activities;
    private readonly IRequestRepository _requests;

    public DataSeeder(
        JsonFileStore store,
        IPasswordHasher hasher,
        IUserRepository users,
        IStudentRepository students,
        IClassRepository classes,
        IAttendanceRepository attendance,
        INotificationRepository notifications,
        IActivityLogRepository activities,
        IRequestRepository requests)
    {
        _store = store;
        _hasher = hasher;
        _users = users;
        _students = students;
        _classes = classes;
        _attendance = attendance;
        _notifications = notifications;
        _activities = activities;
        _requests = requests;
    }

    public async Task SeedAsync()
    {
        foreach (var file in new[]
                 {
                     "users.json", "students.json", "classes.json", "attendance.json",
                     "requests.json", "notifications.json", "activities.json", "parent_alerts.json"
                 })
        {
            var path = _store.GetFilePath(file);
            if (!File.Exists(path))
                await File.WriteAllTextAsync(path, "[]");
        }

        var existingUsers = await _users.GetAllAsync();
        if (existingUsers.Count > 0) return;

        var adminId = Guid.NewGuid().ToString("N");
        await _users.SaveAllAsync(new List<User>
        {
            new()
            {
                Id = adminId,
                Username = "admin",
                Email = "admin@medcollege.edu",
                FullName = "College Admin",
                Role = UserRole.Admin,
                PasswordHash = _hasher.Hash("Admin@123"),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }
        });

        await _classes.SaveAllAsync(Array.Empty<ClassRoom>());
        await _students.SaveAllAsync(Array.Empty<Student>());
        await _attendance.SaveAllAsync(Array.Empty<AttendanceRecord>());
        await _requests.SaveAllAsync(Array.Empty<ProfileUpdateRequest>());
        await _notifications.SaveAllAsync(new List<AppNotification>
        {
            new()
            {
                UserId = adminId,
                Title = "Get started",
                Message = "Create a class (batch), then add students to that class. Face cameras are per class.",
                Type = NotificationType.AdminAnnouncement,
                LinkUrl = "/Admin/CreateClass"
            }
        });
        await _activities.SaveAllAsync(new List<ActivityLog>
        {
            new()
            {
                ActorUserId = adminId,
                ActorName = "College Admin",
                Action = "Seed",
                Description = "Seeded admin account only."
            }
        });
    }
}
