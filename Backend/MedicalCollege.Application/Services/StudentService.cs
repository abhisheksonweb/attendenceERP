using MedicalCollege.Application.Common;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;

namespace MedicalCollege.Application.Services;

public class StudentService : IStudentService
{
    private static readonly string[] ProtectedFields =
    [
        "Name",
        "Department",
        "Semester",
        "Course",
        "EnrollmentNumber",
        "DateOfBirth",
        "Gender"
    ];

    private readonly IUserRepository _userRepository;
    private readonly IStudentRepository _studentRepository;
    private readonly IClassRepository _classRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IActivityService _activityService;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;

    public StudentService(
        IUserRepository userRepository,
        IStudentRepository studentRepository,
        IClassRepository classRepository,
        IPasswordHasher passwordHasher,
        IActivityService activityService,
        INotificationService notificationService,
        IEmailService emailService)
    {
        _userRepository = userRepository;
        _studentRepository = studentRepository;
        _classRepository = classRepository;
        _passwordHasher = passwordHasher;
        _activityService = activityService;
        _notificationService = notificationService;
        _emailService = emailService;
    }

    public IReadOnlyList<string> GetProtectedFieldNames() => ProtectedFields;

    public async Task<PagedResult<StudentFormViewModel>> SearchAsync(StudentListFilter filter, CancellationToken cancellationToken = default)
    {
        filter ??= new StudentListFilter();
        var students = await _studentRepository.GetAllAsync();
        IEnumerable<Student> query = students;

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(s =>
                (!string.IsNullOrEmpty(s.Name) && s.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.StudentId) && s.StudentId.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.EnrollmentNumber) && s.EnrollmentNumber.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.Email) && s.Email.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.Mobile) && s.Mobile.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Department))
        {
            var dept = filter.Department.Trim();
            query = query.Where(s =>
                !string.IsNullOrEmpty(s.Department) &&
                s.Department.Contains(dept, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.Course))
        {
            var course = filter.Course.Trim();
            query = query.Where(s =>
                !string.IsNullOrEmpty(s.Course) &&
                s.Course.Contains(course, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.Semester))
        {
            var sem = filter.Semester.Trim();
            query = query.Where(s =>
                !string.IsNullOrEmpty(s.Semester) &&
                s.Semester.Contains(sem, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.IsActive.HasValue)
            query = query.Where(s => s.IsActive == filter.IsActive.Value);

        var sortBy = (filter.SortBy ?? "name").Trim().ToLowerInvariant();
        var desc = string.Equals(filter.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = sortBy switch
        {
            "studentid" or "id" => desc
                ? query.OrderByDescending(s => s.StudentId)
                : query.OrderBy(s => s.StudentId),
            "department" => desc
                ? query.OrderByDescending(s => s.Department)
                : query.OrderBy(s => s.Department),
            "course" => desc
                ? query.OrderByDescending(s => s.Course)
                : query.OrderBy(s => s.Course),
            "semester" => desc
                ? query.OrderByDescending(s => s.Semester)
                : query.OrderBy(s => s.Semester),
            "status" => desc
                ? query.OrderByDescending(s => s.IsActive)
                : query.OrderBy(s => s.IsActive),
            _ => desc
                ? query.OrderByDescending(s => s.Name)
                : query.OrderBy(s => s.Name)
        };

        var ordered = query.ToList();
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize <= 0 ? 10 : filter.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var users = await _userRepository.GetAllAsync();
        var userLookup = users.ToDictionary(u => u.Id);

        return new PagedResult<StudentFormViewModel>
        {
            Items = ordered
                .Skip(skip)
                .Take(pageSize)
                .Select(s => MapToFormViewModel(s, userLookup.GetValueOrDefault(s.UserId)))
                .ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = ordered.Count
        };
    }

    public async Task<StudentFormViewModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByIdAsync(id);
        if (student is null)
            return null;

        var user = await _userRepository.GetByIdAsync(student.UserId);
        return MapToFormViewModel(student, user);
    }

    public async Task<StudentFormViewModel?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByUserIdAsync(userId);
        if (student is null)
            return null;

        var user = await _userRepository.GetByIdAsync(student.UserId);
        return MapToFormViewModel(student, user);
    }

    public async Task<StudentProfileViewModel?> GetProfileAsync(string userId, CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByUserIdAsync(userId);
        return student is null ? null : MapToProfileViewModel(student);
    }

    public async Task<ServiceResult<StudentFormViewModel>> CreateStudentAsync(
        StudentFormViewModel model,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.ClassId))
            return ServiceResult<StudentFormViewModel>.Fail("Select a class (batch). Students must belong to a class.");

        var classRoom = await _classRepository.GetByIdAsync(model.ClassId.Trim());
        if (classRoom is null || !classRoom.IsActive)
            return ServiceResult<StudentFormViewModel>.Fail("Class not found or inactive.");

        // Prefer form values; fall back to class so batch stays consistent when blank.
        if (string.IsNullOrWhiteSpace(model.Course))
            model.Course = classRoom.Course;
        if (string.IsNullOrWhiteSpace(model.Department))
            model.Department = classRoom.Department;
        if (string.IsNullOrWhiteSpace(model.Semester))
            model.Semester = classRoom.Semester;
        model.ClassId = classRoom.Id;

        // Always allocate the next free ID from the highest existing YY-Course-### sequence.
        model.StudentId = await GenerateNextStudentIdAsync(model.Course, model.Department, cancellationToken);
        model.EnrollmentNumber = model.StudentId;

        model.Username = await EnsureUniqueUsernameAsync(
            DeriveUsernameFromEmail(model.Email), cancellationToken);

        var validation = await ValidateStudentFormAsync(model, isCreate: true, cancellationToken);
        if (!validation.Success)
            return ServiceResult<StudentFormViewModel>.Fail(validation.Message!);

        var temporaryPassword = string.IsNullOrWhiteSpace(model.TemporaryPassword)
            ? GenerateTemporaryPassword()
            : model.TemporaryPassword.Trim();

        var user = new User
        {
            Username = model.Username.Trim(),
            Email = model.Email.Trim().ToLowerInvariant(),
            FullName = model.Name.Trim(),
            PasswordHash = _passwordHasher.Hash(temporaryPassword),
            Role = UserRole.Student,
            IsActive = true,
            MustChangePassword = model.ForcePasswordChange,
            ProfilePhotoPath = model.ProfilePhotoPath,
            CreatedBy = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);

        var student = new Student
        {
            UserId = user.Id,
            StudentId = model.StudentId.Trim(),
            EnrollmentNumber = model.EnrollmentNumber.Trim(),
            Name = model.Name.Trim(),
            Email = model.Email.Trim(),
            Course = model.Course.Trim(),
            Department = model.Department.Trim(),
            Semester = model.Semester.Trim(),
            ClassId = classRoom.Id,
            Mobile = model.Mobile.Trim(),
            DateOfBirth = model.DateOfBirth.Date,
            Gender = model.Gender,
            GuardianName = model.GuardianName?.Trim(),
            GuardianPhone = model.GuardianPhone?.Trim(),
            GuardianEmail = model.GuardianEmail?.Trim(),
            ProfilePhotoPath = model.ProfilePhotoPath,
            IsActive = model.IsActive,
            FaceRegistered = false,
            CreatedBy = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        await _studentRepository.AddAsync(student);

        await _notificationService.CreateAsync(
            user.Id,
            "Welcome to Medical College Attendance",
            $"Your student account has been created. Student ID: {student.StudentId}. Login email: {user.Email}. Temporary password: {temporaryPassword}.",
            NotificationType.AccountCreated,
            "/Account/Login",
            cancellationToken);

        var admin = await _userRepository.GetByIdAsync(actorUserId);
        var adminEmail = admin?.Email?.Trim() ?? string.Empty;
        var adminName = string.IsNullOrWhiteSpace(admin?.FullName) ? actorName : admin!.FullName;

        var (emailSent, emailDetail) = await _emailService.SendStudentWelcomeAsync(
            user.Email,
            student.Name,
            student.StudentId,
            temporaryPassword,
            adminEmail,
            adminName,
            cancellationToken);

        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "Student Created",
            $"Created student {student.Name} ({student.StudentId}) in class {classRoom.Name}. Email from {adminEmail} to {user.Email}: {(emailSent ? "sent" : "not sent")} — {emailDetail}");

        var result = MapToFormViewModel(student, user);
        result.TemporaryPassword = temporaryPassword;
        var message = emailSent
            ? "Student added. Mail sent."
            : $"Student added. Mail not sent ({emailDetail})";
        return ServiceResult<StudentFormViewModel>.Ok(result, message);
    }

    public async Task<ServiceResult<StudentImportResultViewModel>> ImportFromCsvAsync(
        Stream csvStream,
        string? defaultClassId,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(csvStream);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            return ServiceResult<StudentImportResultViewModel>.Fail("CSV is empty.");

        var headers = ParseCsvLine(headerLine)
            .Select(h => h.Trim().ToLowerInvariant())
            .ToList();
        if (headers.Count == 0)
            return ServiceResult<StudentImportResultViewModel>.Fail("CSV header row is missing.");

        var col = BuildColumnMap(headers);
        if (!col.ContainsKey("name") || !col.ContainsKey("email"))
            return ServiceResult<StudentImportResultViewModel>.Fail("CSV must include Name and Email columns.");

        ClassRoom? defaultClass = null;
        if (!string.IsNullOrWhiteSpace(defaultClassId))
        {
            defaultClass = await _classRepository.GetByIdAsync(defaultClassId.Trim());
            if (defaultClass is null || !defaultClass.IsActive)
                return ServiceResult<StudentImportResultViewModel>.Fail("Default class not found or inactive.");
        }

        var classCache = new Dictionary<string, ClassRoom?>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<StudentImportRowResult>();
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowNumber = 1;
        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var cells = ParseCsvLine(line);
            string Cell(string key) =>
                col.TryGetValue(key, out var i) && i < cells.Count ? cells[i].Trim() : string.Empty;

            var name = Cell("name");
            var email = Cell("email");
            var classCode = Cell("classcode");
            var mobile = Cell("mobile");
            var studentId = Cell("studentid");
            var enrollment = Cell("enrollmentnumber");
            var dobRaw = Cell("dateofbirth");
            var genderRaw = Cell("gender");
            var guardianName = Cell("guardianname");
            var guardianPhone = Cell("guardianphone");
            var guardianEmail = Cell("guardianemail");
            var photoUrl = Cell("photourl");

            var rowResult = new StudentImportRowResult
            {
                RowNumber = rowNumber,
                Name = name,
                Email = email,
                ClassCode = string.IsNullOrWhiteSpace(classCode) ? null : classCode,
                PhotoUrl = string.IsNullOrWhiteSpace(photoUrl) ? null : photoUrl
            };

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
            {
                rowResult.Success = false;
                rowResult.Message = "Name and Email are required.";
                rows.Add(rowResult);
                continue;
            }

            if (string.IsNullOrWhiteSpace(mobile))
            {
                rowResult.Success = false;
                rowResult.Message = "Mobile is required.";
                rows.Add(rowResult);
                continue;
            }

            ClassRoom? classRoom;
            var classWasCreated = false;
            if (!string.IsNullOrWhiteSpace(classCode))
            {
                var codeKey = classCode.Trim();
                if (!classCache.TryGetValue(codeKey, out classRoom))
                {
                    classRoom = await _classRepository.GetByCodeAsync(codeKey);
                    if (classRoom is null)
                    {
                        var courseCol = Cell("course");
                        var deptCol = Cell("department");
                        var semCol = Cell("semester");
                        classRoom = await CreateClassFromImportAsync(
                            codeKey, courseCol, deptCol, semCol, actorUserId, actorName, cancellationToken);
                        classWasCreated = true;
                    }
                    else if (!classRoom.IsActive)
                    {
                        classRoom.IsActive = true;
                        classRoom.UpdatedAt = DateTime.UtcNow;
                        await _classRepository.UpdateAsync(classRoom);
                        classWasCreated = true; // reactivated for import messaging
                    }

                    classCache[codeKey] = classRoom;
                }

                if (classRoom is null)
                {
                    rowResult.Success = false;
                    rowResult.Message = $"Could not create class '{codeKey}'.";
                    rows.Add(rowResult);
                    continue;
                }
            }
            else if (defaultClass is not null)
            {
                classRoom = defaultClass;
                rowResult.ClassCode = defaultClass.Code;
            }
            else
            {
                rowResult.Success = false;
                rowResult.Message = "No ClassCode in row and no default class selected.";
                rows.Add(rowResult);
                continue;
            }

            var dob = DateTime.Today.AddYears(-18);
            if (!string.IsNullOrWhiteSpace(dobRaw) && !DateTime.TryParse(dobRaw, out dob))
            {
                rowResult.Success = false;
                rowResult.Message = $"Invalid DateOfBirth '{dobRaw}'.";
                rows.Add(rowResult);
                continue;
            }

            var gender = Gender.Male;
            if (!string.IsNullOrWhiteSpace(genderRaw) && !Enum.TryParse(genderRaw, ignoreCase: true, out gender))
            {
                rowResult.Success = false;
                rowResult.Message = $"Invalid Gender '{genderRaw}' (use Male, Female, or Other).";
                rows.Add(rowResult);
                continue;
            }

            var model = new StudentFormViewModel
            {
                ClassId = classRoom.Id,
                Name = name,
                Email = email,
                Mobile = mobile,
                StudentId = studentId,
                EnrollmentNumber = enrollment,
                DateOfBirth = dob.Date,
                Gender = gender,
                GuardianName = string.IsNullOrWhiteSpace(guardianName) ? null : guardianName,
                GuardianPhone = string.IsNullOrWhiteSpace(guardianPhone) ? null : guardianPhone,
                GuardianEmail = string.IsNullOrWhiteSpace(guardianEmail) ? null : guardianEmail,
                ProfilePhotoPath = string.IsNullOrWhiteSpace(photoUrl) ? null : photoUrl.Trim(),
                TemporaryPassword = "Temp@123",
                ForcePasswordChange = true,
                IsActive = true
            };

            var create = await CreateStudentAsync(model, actorUserId, actorName, cancellationToken);
            rowResult.Success = create.Success;
            if (create.Success)
            {
                rowResult.PortalStudentId = create.Data?.Id;
                rowResult.ClassId = classRoom.Id;
                rowResult.Message = classWasCreated
                    ? $"Class {classRoom.Code} created; student added."
                    : $"Added to {classRoom.Name} ({classRoom.Code}).";
                if (!string.IsNullOrWhiteSpace(photoUrl))
                    rowResult.Message += " Photo link saved.";
                affected.Add(classRoom.Id);
            }
            else
            {
                rowResult.Message = create.Message ?? "Failed.";
            }
            rows.Add(rowResult);
        }

        if (rows.Count == 0)
            return ServiceResult<StudentImportResultViewModel>.Fail("CSV has no data rows.");

        var result = new StudentImportResultViewModel
        {
            TotalRows = rows.Count,
            SuccessCount = rows.Count(r => r.Success),
            FailedCount = rows.Count(r => !r.Success),
            AffectedClassIds = affected.ToList(),
            Rows = rows
        };

        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "Student CSV Import",
            $"Imported {result.SuccessCount}/{result.TotalRows} students from CSV.");

        return ServiceResult<StudentImportResultViewModel>.Ok(result,
            $"Import finished: {result.SuccessCount} added, {result.FailedCount} failed.");
    }

    public async Task<ServiceResult<StudentFormViewModel>> UpdateStudentAsync(
        StudentFormViewModel model,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Id))
            return ServiceResult<StudentFormViewModel>.Fail("Student id is required.");

        var student = await _studentRepository.GetByIdAsync(model.Id);
        if (student is null)
            return ServiceResult<StudentFormViewModel>.Fail("Student not found.");

        var validation = await ValidateStudentFormAsync(model, isCreate: false, cancellationToken);
        if (!validation.Success)
            return ServiceResult<StudentFormViewModel>.Fail(validation.Message!);

        var user = await _userRepository.GetByIdAsync(student.UserId);
        if (user is null)
            return ServiceResult<StudentFormViewModel>.Fail("Linked user account not found.");

        student.StudentId = model.StudentId.Trim();
        student.EnrollmentNumber = model.EnrollmentNumber.Trim();
        student.Name = model.Name.Trim();
        student.Email = model.Email.Trim();
        student.Course = model.Course.Trim();
        student.Department = model.Department.Trim();
        student.Semester = model.Semester.Trim();
        student.ClassId = string.IsNullOrWhiteSpace(model.ClassId) ? null : model.ClassId.Trim();
        student.Mobile = model.Mobile.Trim();
        student.DateOfBirth = model.DateOfBirth.Date;
        student.Gender = model.Gender;
        student.GuardianName = model.GuardianName?.Trim();
        student.GuardianPhone = model.GuardianPhone?.Trim();
        student.GuardianEmail = model.GuardianEmail?.Trim();
        student.ProfilePhotoPath = model.ProfilePhotoPath;
        student.IsActive = model.IsActive;
        student.UpdatedAt = DateTime.UtcNow;

        // Username is not edited on the form; keep existing login handle.
        user.Email = model.Email.Trim();
        user.FullName = model.Name.Trim();
        user.IsActive = model.IsActive;
        user.ProfilePhotoPath = model.ProfilePhotoPath;
        user.UpdatedAt = DateTime.UtcNow;

        await _studentRepository.UpdateAsync(student);
        await _userRepository.UpdateAsync(user);

        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "Student Updated",
            $"Updated student record for {student.Name} ({student.StudentId}).");

        return ServiceResult<StudentFormViewModel>.Ok(MapToFormViewModel(student, user), "Student updated successfully.");
    }

    public async Task<ServiceResult> DeactivateStudentAsync(
        string id,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByIdAsync(id);
        if (student is null)
            return ServiceResult.Fail("Student not found.");

        if (!student.IsActive)
            return ServiceResult.Ok("Student is already deactivated.");

        var user = await _userRepository.GetByIdAsync(student.UserId);
        if (user is null)
            return ServiceResult.Fail("Linked user account not found.");

        student.IsActive = false;
        student.UpdatedAt = DateTime.UtcNow;
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        await _studentRepository.UpdateAsync(student);
        await _userRepository.UpdateAsync(user);

        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "Student Deactivated",
            $"Deactivated student {student.Name} ({student.StudentId}).");

        return ServiceResult.Ok("Student deactivated successfully.");
    }

    public async Task<ServiceResult> DeleteStudentAsync(
        string id,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByIdAsync(id);
        if (student is null)
            return ServiceResult.Fail("Student not found.");

        var name = student.Name;
        var code = student.StudentId;
        var userId = student.UserId;

        await _studentRepository.DeleteAsync(student.Id);

        var user = await _userRepository.GetByIdAsync(userId);
        if (user is not null)
            await _userRepository.DeleteAsync(user.Id);

        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "Student Deleted",
            $"Deleted student {name} ({code}).");

        return ServiceResult.Ok("Student deleted successfully.");
    }

    public async Task<ServiceResult<string>> ResetStudentPasswordAsync(
        string id,
        string newPassword,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            return ServiceResult<string>.Fail("Password must be at least 6 characters.");

        var student = await _studentRepository.GetByIdAsync(id);
        if (student is null)
            return ServiceResult<string>.Fail("Student not found.");

        var user = await _userRepository.GetByIdAsync(student.UserId);
        if (user is null)
            return ServiceResult<string>.Fail("Linked user account not found.");

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        user.MustChangePassword = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        await _notificationService.CreateAsync(
            user.Id,
            "Password Reset",
            "Your password has been reset by an administrator. Please sign in with your new password and change it immediately.",
            NotificationType.PasswordReset,
            "/Account/Login",
            cancellationToken);

        var admin = await _userRepository.GetByIdAsync(actorUserId);
        var (emailSent, emailDetail) = await _emailService.SendPasswordResetAsync(
            user.Email,
            student.Name,
            newPassword,
            admin?.Email,
            string.IsNullOrWhiteSpace(admin?.FullName) ? actorName : admin!.FullName,
            cancellationToken);

        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "Student Password Reset",
            $"Reset password for student {student.Name} ({student.StudentId}). Email: {(emailSent ? "sent" : "not sent")}.");

        var msg = emailSent
            ? $"Password reset. New password emailed to {user.Email}."
            : $"Password reset successfully. {emailDetail}";
        return ServiceResult<string>.Ok(newPassword, msg);
    }

    public async Task<ServiceResult<StudentProfileViewModel>> UpdateEditableProfileAsync(
        string userId,
        StudentProfileViewModel model,
        CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByUserIdAsync(userId);
        if (student is null)
            return ServiceResult<StudentProfileViewModel>.Fail("Student profile not found.");

        student.Mobile = model.Mobile.Trim();
        student.EmergencyContact = model.EmergencyContact?.Trim();
        student.Address = model.Address?.Trim();
        student.City = model.City?.Trim();
        student.State = model.State?.Trim();
        student.Pincode = model.Pincode?.Trim();
        student.GuardianName = model.GuardianName?.Trim();
        student.GuardianPhone = model.GuardianPhone?.Trim();
        student.GuardianEmail = model.GuardianEmail?.Trim();

        if (!string.IsNullOrWhiteSpace(model.ProfilePhotoPath))
            student.ProfilePhotoPath = model.ProfilePhotoPath.Trim();

        student.UpdatedAt = DateTime.UtcNow;
        await _studentRepository.UpdateAsync(student);

        var profile = MapToProfileViewModel(student);
        return ServiceResult<StudentProfileViewModel>.Ok(profile, "Profile updated successfully.");
    }

    public async Task<ServiceResult> MarkFaceEnrolledAsync(string studentId, int? frmStudentId, CancellationToken cancellationToken = default)
    {
        var student = await _studentRepository.GetByIdAsync(studentId);
        if (student is null)
            return ServiceResult.Fail("Student not found.");

        student.FaceRegistered = true;
        if (frmStudentId.HasValue)
            student.FrmStudentId = frmStudentId;
        student.UpdatedAt = DateTime.UtcNow;
        await _studentRepository.UpdateAsync(student);

        await _notificationService.CreateAsync(
            student.UserId,
            "Face Enrolled",
            "Your face has been registered successfully for attendance recognition.",
            NotificationType.General,
            "/Student/FaceEnrollment",
            cancellationToken);

        return ServiceResult.Ok("Face enrolled successfully.");
    }

    public async Task<string> GenerateNextStudentIdAsync(
        string course,
        string department,
        CancellationToken cancellationToken = default)
    {
        var year = DateTime.Now.Year % 100;
        var courseToken = NormalizeCourseToken(course);
        var prefix = $"{year:D2}-{courseToken}-";

        // Sequence is global per YY-Course-### (not per department), so we never reuse an ID.
        var students = await _studentRepository.GetAllAsync();
        var maxSeq = 0;
        foreach (var s in students)
        {
            if (string.IsNullOrWhiteSpace(s.StudentId))
                continue;
            if (!s.StudentId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var tail = s.StudentId[prefix.Length..];
            if (int.TryParse(tail, out var n) && n > maxSeq)
                maxSeq = n;
        }

        return $"{prefix}{(maxSeq + 1):D3}";
    }

    public int CalculateProfileCompletionPercent(StudentProfileViewModel profile)
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

    private async Task<ServiceResult> ValidateStudentFormAsync(
        StudentFormViewModel model,
        bool isCreate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.ClassId))
            return ServiceResult.Fail("Class is required.");

        if (string.IsNullOrWhiteSpace(model.StudentId))
            return ServiceResult.Fail("Student ID is required.");

        if (string.IsNullOrWhiteSpace(model.EnrollmentNumber))
            return ServiceResult.Fail("Enrollment number is required.");

        if (string.IsNullOrWhiteSpace(model.Name))
            return ServiceResult.Fail("Name is required.");

        if (string.IsNullOrWhiteSpace(model.Email))
            return ServiceResult.Fail("Email is required.");

        if (string.IsNullOrWhiteSpace(model.Department) || string.IsNullOrWhiteSpace(model.Course))
            return ServiceResult.Fail("Class must have Department and Course set.");

        if (isCreate && !string.IsNullOrWhiteSpace(model.TemporaryPassword) && model.TemporaryPassword.Length < 6)
            return ServiceResult.Fail("Temporary password must be at least 6 characters.");

        string? linkedUserId = null;
        if (!isCreate && !string.IsNullOrWhiteSpace(model.Id))
        {
            var existingStudent = await _studentRepository.GetByIdAsync(model.Id);
            linkedUserId = existingStudent?.UserId;
        }

        var existingStudentId = await _studentRepository.GetByStudentIdAsync(model.StudentId.Trim());
        if (existingStudentId is not null && existingStudentId.Id != model.Id)
            return ServiceResult.Fail("Student ID is already in use.");

        var existingEnrollment = await _studentRepository.GetByEnrollmentAsync(model.EnrollmentNumber.Trim());
        if (existingEnrollment is not null && existingEnrollment.Id != model.Id)
            return ServiceResult.Fail("Enrollment number is already in use.");

        if (!string.IsNullOrWhiteSpace(model.Username))
        {
            var existingUsername = await _userRepository.GetByUsernameAsync(model.Username.Trim());
            if (existingUsername is not null && existingUsername.Id != linkedUserId)
                return ServiceResult.Fail("Username is already in use.");
        }

        var existingEmail = await _userRepository.GetByEmailAsync(model.Email.Trim());
        if (existingEmail is not null && existingEmail.Id != linkedUserId)
            return ServiceResult.Fail("Email is already in use.");

        return ServiceResult.Ok();
    }

    private async Task<ClassRoom> CreateClassFromImportAsync(
        string classCode,
        string courseFromCsv,
        string departmentFromCsv,
        string semesterFromCsv,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken)
    {
        var code = classCode.Trim().ToUpperInvariant();
        var parts = code.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var course = !string.IsNullOrWhiteSpace(courseFromCsv)
            ? courseFromCsv.Trim()
            : (parts.Length > 0 ? parts[0] : "GEN");

        var semester = !string.IsNullOrWhiteSpace(semesterFromCsv)
            ? semesterFromCsv.Trim()
            : (parts.Length > 1 ? string.Join("-", parts.Skip(1)) : "1");

        var department = !string.IsNullOrWhiteSpace(departmentFromCsv)
            ? departmentFromCsv.Trim()
            : InferDepartment(course);

        var entity = new ClassRoom
        {
            Name = code,
            Code = code,
            Course = course,
            Department = department,
            Semester = semester,
            Description = "Auto-created from student CSV import",
            AdminUserId = actorUserId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _classRepository.AddAsync(entity);
        await _activityService.LogAsync(
            actorUserId,
            actorName,
            "CreateClass",
            $"Auto-created class {entity.Name} ({entity.Code}) during CSV import");
        return entity;
    }

    private static string InferDepartment(string course)
    {
        var c = course.Trim().ToUpperInvariant();
        if (c.Contains("MBBS") || c.Contains("MD") || c.Contains("MS")) return "Medicine";
        if (c.Contains("BDS") || c.Contains("MDS")) return "Dentistry";
        if (c.Contains("BSC") || c.Contains("NURS")) return "Nursing";
        return "General";
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = ["name", "fullname", "studentname"],
            ["email"] = ["email", "emailaddress"],
            ["mobile"] = ["mobile", "phone", "mobileno", "phonenumber"],
            ["classcode"] = ["classcode", "class", "batchcode", "code"],
            ["studentid"] = ["studentid", "student_id", "rollno", "roll"],
            ["enrollmentnumber"] = ["enrollmentnumber", "enrollment", "enrollmentno"],
            ["dateofbirth"] = ["dateofbirth", "dob", "birthdate"],
            ["gender"] = ["gender", "sex"],
            ["course"] = ["course"],
            ["department"] = ["department", "dept"],
            ["semester"] = ["semester", "sem", "year"],
            ["guardianname"] = ["guardianname", "parentname"],
            ["guardianphone"] = ["guardianphone", "parentphone"],
            ["guardianemail"] = ["guardianemail", "parentemail"],
            ["photourl"] = ["photourl", "photolink", "photo", "profilephoto", "profilephotourl", "imagelink", "imageurl"]
        };

        for (var i = 0; i < headers.Count; i++)
        {
            var h = headers[i].Replace(" ", string.Empty).Replace("_", string.Empty);
            foreach (var (canonical, names) in aliases)
            {
                if (map.ContainsKey(canonical)) continue;
                if (names.Any(n => n.Equals(h, StringComparison.OrdinalIgnoreCase)
                                   || n.Replace("_", string.Empty).Equals(h, StringComparison.OrdinalIgnoreCase)))
                    map[canonical] = i;
            }
        }

        return map;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private async Task<string> EnsureUniqueUsernameAsync(string preferred, CancellationToken cancellationToken)
    {
        var baseName = string.IsNullOrWhiteSpace(preferred) ? "student" : preferred.Trim().ToLowerInvariant();
        var candidate = baseName;
        var i = 1;
        while (await _userRepository.GetByUsernameAsync(candidate) is not null)
        {
            candidate = $"{baseName}{i}";
            i++;
            if (i > 9999) break;
        }
        return candidate;
    }

    private static string DeriveUsernameFromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "student";
        var local = email.Split('@')[0].Trim().ToLowerInvariant();
        var cleaned = new string(local.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "student" : cleaned;
    }

    /// <summary>Normalize course for IDs, e.g. "B.Tech" / "b tech" → "Btech".</summary>
    public static string NormalizeCourseToken(string? course)
    {
        if (string.IsNullOrWhiteSpace(course)) return "GEN";
        var chars = course.Where(char.IsLetterOrDigit).ToArray();
        if (chars.Length == 0) return "GEN";
        var token = new string(chars);
        return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }

    private static string GenerateTemporaryPassword()
    {
        // Mix upper, lower, digit so it is usable as a temporary login password.
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        Span<char> pwd = stackalloc char[10];
        pwd[0] = upper[Random.Shared.Next(upper.Length)];
        pwd[1] = lower[Random.Shared.Next(lower.Length)];
        pwd[2] = digits[Random.Shared.Next(digits.Length)];
        const string all = upper + lower + digits;
        for (var i = 3; i < pwd.Length; i++)
            pwd[i] = all[Random.Shared.Next(all.Length)];
        // Shuffle
        for (var i = pwd.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (pwd[i], pwd[j]) = (pwd[j], pwd[i]);
        }
        return new string(pwd);
    }

    private StudentProfileViewModel MapToProfileViewModel(Student student)
    {
        var profile = new StudentProfileViewModel
        {
            Id = student.Id,
            StudentId = student.StudentId,
            EnrollmentNumber = student.EnrollmentNumber,
            Name = student.Name,
            Email = student.Email,
            Course = student.Course,
            Department = student.Department,
            Semester = student.Semester,
            DateOfBirth = student.DateOfBirth,
            Gender = student.Gender,
            ProfilePhotoPath = student.ProfilePhotoPath,
            Mobile = student.Mobile,
            EmergencyContact = student.EmergencyContact,
            Address = student.Address,
            City = student.City,
            State = student.State,
            Pincode = student.Pincode,
            GuardianName = student.GuardianName,
            GuardianPhone = student.GuardianPhone,
            GuardianEmail = student.GuardianEmail,
            FaceRegistered = student.FaceRegistered
        };

        profile.ProfileCompletionPercent = CalculateProfileCompletionPercent(profile);
        return profile;
    }

    private static StudentFormViewModel MapToFormViewModel(Student student, User? user = null) => new()
    {
        Id = student.Id,
        StudentId = student.StudentId,
        EnrollmentNumber = student.EnrollmentNumber,
        Name = student.Name,
        Email = student.Email,
        Course = student.Course,
        Department = student.Department,
        Semester = student.Semester,
        ClassId = student.ClassId,
        Mobile = student.Mobile,
        DateOfBirth = student.DateOfBirth,
        Gender = student.Gender,
        Username = user?.Username ?? string.Empty,
        GuardianName = student.GuardianName,
        GuardianPhone = student.GuardianPhone,
        GuardianEmail = student.GuardianEmail,
        ProfilePhotoPath = student.ProfilePhotoPath,
        IsActive = student.IsActive,
        FaceRegistered = student.FaceRegistered,
        FrmStudentId = student.FrmStudentId
    };
}
