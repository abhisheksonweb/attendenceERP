using MedicalCollege.Application.Common;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Interfaces;

namespace MedicalCollege.Application.Services;

public class ClassService : IClassService
{
    private readonly IClassRepository _classes;
    private readonly IStudentRepository _students;
    private readonly IAttendanceRepository _attendance;
    private readonly IUserRepository _users;
    private readonly IActivityService _activity;

    public ClassService(
        IClassRepository classes,
        IStudentRepository students,
        IAttendanceRepository attendance,
        IUserRepository users,
        IActivityService activity)
    {
        _classes = classes;
        _students = students;
        _attendance = attendance;
        _users = users;
        _activity = activity;
    }

    public async Task<IReadOnlyList<ClassFormViewModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var classes = await _classes.GetAllAsync();
        var students = await _students.GetAllAsync();
        return classes
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.Name)
            .Select(c => Map(c, students.Count(s => s.ClassId == c.Id && s.IsActive)))
            .ToList();
    }

    public async Task<ClassFormViewModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var c = await _classes.GetByIdAsync(id);
        if (c is null) return null;
        var students = await _students.GetAllAsync();
        return Map(c, students.Count(s => s.ClassId == c.Id && s.IsActive));
    }

    public async Task<ClassDetailViewModel?> GetDetailAsync(string id, CancellationToken cancellationToken = default)
    {
        var c = await _classes.GetByIdAsync(id);
        if (c is null) return null;

        var students = (await _students.GetAllAsync()).Where(s => s.ClassId == id).OrderBy(s => s.Name).ToList();
        var users = await _users.GetAllAsync();
        var userLookup = users.ToDictionary(u => u.Id);
        var today = await _attendance.GetByDateAsync(DateTime.Today);
        var studentCodes = students.Select(s => s.Id).ToHashSet();

        return new ClassDetailViewModel
        {
            Class = Map(c, students.Count(s => s.IsActive)),
            Students = students.Select(s => MapStudent(s, userLookup.GetValueOrDefault(s.UserId))).ToList(),
            TodayAttendance = today
                .Where(a => studentCodes.Contains(a.StudentId))
                .Select(a => new AttendanceRecordViewModel
                {
                    Id = a.Id,
                    StudentCode = a.StudentCode,
                    StudentName = a.StudentName,
                    Department = a.Department,
                    Course = a.Course,
                    Date = a.Date,
                    Status = a.Status.ToString(),
                    Source = a.Source,
                    Remarks = a.Remarks,
                    FirstIn = a.FirstIn,
                    LastOut = a.LastOut,
                    Duration = a.Duration,
                    DurationSeconds = a.DurationSeconds,
                    EarlyLeave = a.EarlyLeave
                })
                .ToList(),
            FrmSynced = c.FrmClassId.HasValue
        };
    }

    public async Task<ServiceResult<ClassFormViewModel>> CreateAsync(
        ClassFormViewModel model, string actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Code))
            return ServiceResult<ClassFormViewModel>.Fail("Name and code are required.");

        var existing = await _classes.GetByCodeAsync(model.Code.Trim());
        if (existing is not null)
            return ServiceResult<ClassFormViewModel>.Fail("Class code already exists.");

        var entity = new ClassRoom
        {
            Name = model.Name.Trim(),
            Code = model.Code.Trim().ToUpperInvariant(),
            Department = model.Department.Trim(),
            Course = model.Course.Trim(),
            Semester = model.Semester.Trim(),
            Description = model.Description?.Trim(),
            MaxClassDurationMinutes = NormalizeMinutes(model.MaxClassDurationMinutes),
            MinAttendanceMinutes = NormalizeMinutes(model.MinAttendanceMinutes),
            AdminUserId = actorUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _classes.AddAsync(entity);
        await _activity.LogAsync(actorUserId, actorName, "CreateClass", $"Created class {entity.Name} ({entity.Code})");
        return ServiceResult<ClassFormViewModel>.Ok(Map(entity, 0), "Class created.");
    }

    public async Task<ServiceResult<ClassFormViewModel>> UpdateAsync(
        ClassFormViewModel model, string actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
            return ServiceResult<ClassFormViewModel>.Fail("Class id required.");

        var entity = await _classes.GetByIdAsync(model.Id);
        if (entity is null)
            return ServiceResult<ClassFormViewModel>.Fail("Class not found.");

        var byCode = await _classes.GetByCodeAsync(model.Code.Trim());
        if (byCode is not null && byCode.Id != entity.Id)
            return ServiceResult<ClassFormViewModel>.Fail("Class code already exists.");

        entity.Name = model.Name.Trim();
        entity.Code = model.Code.Trim().ToUpperInvariant();
        entity.Department = model.Department.Trim();
        entity.Course = model.Course.Trim();
        entity.Semester = model.Semester.Trim();
        entity.Description = model.Description?.Trim();
        entity.MaxClassDurationMinutes = NormalizeMinutes(model.MaxClassDurationMinutes);
        entity.MinAttendanceMinutes = NormalizeMinutes(model.MinAttendanceMinutes);
        entity.IsActive = model.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _classes.UpdateAsync(entity);
        await _activity.LogAsync(actorUserId, actorName, "UpdateClass", $"Updated class {entity.Name}");
        var students = await _students.GetAllAsync();
        return ServiceResult<ClassFormViewModel>.Ok(Map(entity, students.Count(s => s.ClassId == entity.Id)), "Class updated.");
    }

    public async Task<ServiceResult> DeactivateAsync(string id, string actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var entity = await _classes.GetByIdAsync(id);
        if (entity is null) return ServiceResult.Fail("Class not found.");
        entity.IsActive = false;
        entity.UpdatedAt = DateTime.UtcNow;
        await _classes.UpdateAsync(entity);
        await _activity.LogAsync(actorUserId, actorName, "DeactivateClass", $"Deactivated class {entity.Name}");
        return ServiceResult.Ok("Class deactivated.");
    }

    public async Task SetFrmClassIdAsync(string classId, int frmClassId, CancellationToken cancellationToken = default)
    {
        var entity = await _classes.GetByIdAsync(classId);
        if (entity is null) return;
        entity.FrmClassId = frmClassId;
        entity.UpdatedAt = DateTime.UtcNow;
        await _classes.UpdateAsync(entity);
    }

    private static ClassFormViewModel Map(ClassRoom c, int studentCount) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Code = c.Code,
        Department = c.Department,
        Course = c.Course,
        Semester = c.Semester,
        Description = c.Description,
        MaxClassDurationMinutes = c.MaxClassDurationMinutes,
        MinAttendanceMinutes = c.MinAttendanceMinutes,
        IsActive = c.IsActive,
        FrmClassId = c.FrmClassId,
        StudentCount = studentCount,
        CreatedAt = c.CreatedAt
    };

    private static int? NormalizeMinutes(int? value)
    {
        if (value is null or < 1) return null;
        return Math.Min(value.Value, 24 * 60);
    }

    private static StudentFormViewModel MapStudent(Student s, User? user) => new()
    {
        Id = s.Id,
        StudentId = s.StudentId,
        EnrollmentNumber = s.EnrollmentNumber,
        Name = s.Name,
        Email = s.Email,
        Course = s.Course,
        Department = s.Department,
        Semester = s.Semester,
        ClassId = s.ClassId,
        Mobile = s.Mobile,
        DateOfBirth = s.DateOfBirth,
        Gender = s.Gender,
        Username = user?.Username ?? string.Empty,
        GuardianName = s.GuardianName,
        GuardianPhone = s.GuardianPhone,
        GuardianEmail = s.GuardianEmail,
        ProfilePhotoPath = s.ProfilePhotoPath,
        IsActive = s.IsActive
    };
}
