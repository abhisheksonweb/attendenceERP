using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;

namespace MedicalCollege.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IStudentRepository _studentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IAttendanceService _attendanceService;
    private readonly IRequestRepository _requestRepository;
    private readonly INotificationService _notificationService;
    private readonly IActivityService _activityService;

    public DashboardService(
        IStudentRepository studentRepository,
        IUserRepository userRepository,
        IAttendanceRepository attendanceRepository,
        IAttendanceService attendanceService,
        IRequestRepository requestRepository,
        INotificationService notificationService,
        IActivityService activityService)
    {
        _studentRepository = studentRepository;
        _userRepository = userRepository;
        _attendanceRepository = attendanceRepository;
        _attendanceService = attendanceService;
        _requestRepository = requestRepository;
        _notificationService = notificationService;
        _activityService = activityService;
    }

    public async Task<DashboardStatsViewModel> GetAdminDashboardAsync(string userId, CancellationToken cancellationToken = default)
    {
        var stats = await BuildCommonDashboardStatsAsync(userId, cancellationToken);
        stats.TotalAdmins = 0;
        stats.ActiveAdmins = 0;
        return stats;
    }

    public async Task<DashboardStatsViewModel> GetSuperAdminDashboardAsync(CancellationToken cancellationToken = default)
    {
        var admins = await _userRepository.GetByRoleAsync(UserRole.Admin);
        var stats = await BuildCommonDashboardStatsAsync(null, cancellationToken);
        stats.TotalAdmins = admins.Count;
        stats.ActiveAdmins = admins.Count(a => a.IsActive);
        return stats;
    }

    public async Task<StudentDashboardViewModel> GetStudentDashboardAsync(string userId, CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByUserIdAsync(userId)
            ?? throw new InvalidOperationException("Student profile not found.");

        var records = await _attendanceService.GetByStudentAsync(student.Id, cancellationToken);
        var stats = await _attendanceService.GetStatsAsync(student.Id, cancellationToken);
        var recent = records
            .OrderByDescending(r => r.Date)
            .Take(10)
            .ToList();

        var profile = new StudentProfileViewModel
        {
            Mobile = student.Mobile,
            EmergencyContact = student.EmergencyContact,
            Address = student.Address,
            City = student.City,
            State = student.State,
            Pincode = student.Pincode,
            GuardianName = student.GuardianName,
            GuardianPhone = student.GuardianPhone,
            ProfilePhotoPath = student.ProfilePhotoPath
        };

        var profileCompletion = CalculateProfileCompletionPercent(profile);

        return new StudentDashboardViewModel
        {
            Name = student.Name,
            AttendancePercentage = stats.Percentage,
            PresentDays = stats.PresentCount,
            AbsentDays = stats.AbsentCount,
            TotalDays = stats.TotalCount,
            ProfileCompletionPercent = profileCompletion,
            FaceRegistered = student.FaceRegistered,
            UnreadNotifications = await _notificationService.GetUnreadCountAsync(userId, cancellationToken),
            RecentAttendance = recent
        };
    }

    private async Task<DashboardStatsViewModel> BuildCommonDashboardStatsAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        var students = await _studentRepository.GetAllAsync();
        var activeStudents = students.Where(s => s.IsActive).ToList();
        var today = DateTime.Today;
        var todayRecords = await _attendanceRepository.GetByDateAsync(today);

        var presentToday = todayRecords.Count(r => r.Status == AttendanceStatus.Present);
        var absentToday = todayRecords.Count(r => r.Status == AttendanceStatus.Absent);
        var pendingRequests = await _requestRepository.GetByStatusAsync(RequestStatus.Pending);
        var allRecords = await _attendanceRepository.GetAllAsync();
        var overallStats = AttendanceService.CalculateStats(allRecords);
        var weeklyChart = await BuildWeeklyAttendanceChartAsync(cancellationToken);
        var recentActivities = await _activityService.GetRecentAsync(10, cancellationToken);

        var unreadNotifications = userId is null
            ? 0
            : await _notificationService.GetUnreadCountAsync(userId, cancellationToken);

        var unknownFaces = activeStudents.Count(s =>
            !s.FaceRegistered &&
            todayRecords.Any(r => r.StudentId == s.Id && r.Status == AttendanceStatus.Absent));

        return new DashboardStatsViewModel
        {
            TotalStudents = activeStudents.Count,
            PresentToday = presentToday,
            AbsentToday = absentToday,
            PendingFaceRegistration = activeStudents.Count(s => !s.FaceRegistered),
            PendingProfileRequests = pendingRequests.Count,
            UnreadNotifications = unreadNotifications,
            AttendancePercentage = overallStats.Percentage,
            UnknownFaces = unknownFaces,
            WeeklyAttendance = weeklyChart,
            RecentActivities = recentActivities
        };
    }

    private async Task<IReadOnlyList<ChartPoint>> BuildWeeklyAttendanceChartAsync(CancellationToken cancellationToken)
    {
        var chart = new List<ChartPoint>();

        for (var i = 6; i >= 0; i--)
        {
            var date = DateTime.Today.AddDays(-i);
            var dayRecords = await _attendanceRepository.GetByDateAsync(date);
            var stats = AttendanceService.CalculateStats(dayRecords);

            chart.Add(new ChartPoint
            {
                Label = date.ToString("ddd"),
                Value = stats.Percentage
            });
        }

        return chart;
    }

    private static int CalculateProfileCompletionPercent(StudentProfileViewModel profile)
    {
        var fields = new[]
        {
            profile.Mobile,
            profile.EmergencyContact,
            profile.Address,
            profile.City,
            profile.State,
            profile.Pincode,
            profile.GuardianName,
            profile.GuardianPhone,
            profile.ProfilePhotoPath
        };

        var filled = fields.Count(f => !string.IsNullOrWhiteSpace(f));
        return (int)Math.Round(filled / (double)fields.Length * 100);
    }
}
