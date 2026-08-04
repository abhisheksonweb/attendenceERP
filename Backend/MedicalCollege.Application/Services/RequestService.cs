using MedicalCollege.Application.Common;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Entities;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Domain.Interfaces;

namespace MedicalCollege.Application.Services;

public class RequestService : IRequestService
{
    public const string AttendanceField = "Attendance";
    public const string AttendanceTimingField = "AttendanceTiming";

    private readonly IRequestRepository _requestRepository;
    private readonly IStudentRepository _studentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly INotificationService _notificationService;
    private readonly IActivityService _activityService;
    private readonly IStudentService _studentService;

    public RequestService(
        IRequestRepository requestRepository,
        IStudentRepository studentRepository,
        IUserRepository userRepository,
        IAttendanceRepository attendanceRepository,
        INotificationService notificationService,
        IActivityService activityService,
        IStudentService studentService)
    {
        _requestRepository = requestRepository;
        _studentRepository = studentRepository;
        _userRepository = userRepository;
        _attendanceRepository = attendanceRepository;
        _notificationService = notificationService;
        _activityService = activityService;
        _studentService = studentService;
    }

    public async Task<ServiceResult<ReviewRequestViewModel>> CreateRequestAsync(
        string studentUserId,
        ProtectedFieldRequestViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.FieldName))
            return ServiceResult<ReviewRequestViewModel>.Fail("Field name is required.");

        if (string.IsNullOrWhiteSpace(model.NewValue))
            return ServiceResult<ReviewRequestViewModel>.Fail("Requested value is required.");

        var normalizedField = NormalizeFieldName(model.FieldName);
        if (!_studentService.GetProtectedFieldNames().Contains(normalizedField, StringComparer.OrdinalIgnoreCase))
            return ServiceResult<ReviewRequestViewModel>.Fail("This field cannot be updated via request.");

        var student = await _studentRepository.GetByUserIdAsync(studentUserId);
        if (student is null)
            return ServiceResult<ReviewRequestViewModel>.Fail("Student not found.");

        var oldValue = GetFieldValue(student, normalizedField);
        if (string.Equals(oldValue, model.NewValue.Trim(), StringComparison.OrdinalIgnoreCase))
            return ServiceResult<ReviewRequestViewModel>.Fail("Requested value matches the current value.");

        var pending = await _requestRepository.GetByStudentAsync(student.Id);
        if (pending.Any(r => r.Status == RequestStatus.Pending &&
                             r.FieldName.Equals(normalizedField, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<ReviewRequestViewModel>.Fail("A pending request already exists for this field.");

        var request = new ProfileUpdateRequest
        {
            StudentId = student.Id,
            StudentUserId = student.UserId,
            StudentName = student.Name,
            FieldName = normalizedField,
            OldValue = oldValue,
            NewValue = model.NewValue.Trim(),
            Status = RequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _requestRepository.AddAsync(request);
        return ServiceResult<ReviewRequestViewModel>.Ok(MapToViewModel(request), "Update request submitted.");
    }

    public async Task<ServiceResult<ReviewRequestViewModel>> CreateAttendanceCorrectionAsync(
        string studentUserId,
        AttendanceCorrectionRequestViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Trim().Length < 5)
            return ServiceResult<ReviewRequestViewModel>.Fail("Please provide a reason (at least 5 characters).");

        var student = await _studentRepository.GetByUserIdAsync(studentUserId);
        if (student is null)
            return ServiceResult<ReviewRequestViewModel>.Fail("Student not found.");

        var date = model.Date.Date;
        var dateKey = date.ToString("yyyy-MM-dd");
        var isTiming = string.Equals(model.RequestKind, "TimingCorrection", StringComparison.OrdinalIgnoreCase);

        var dayRecords = await _attendanceRepository.GetByDateAsync(date);
        var record = dayRecords.FirstOrDefault(r => r.StudentId == student.Id);
        if (record is null)
            return ServiceResult<ReviewRequestViewModel>.Fail("No attendance record found for that date.");

        if (isTiming)
        {
            if (record.Status != AttendanceStatus.Present && record.Status != AttendanceStatus.Late)
                return ServiceResult<ReviewRequestViewModel>.Fail("Timing regularization is only for Present/Late days.");

            if (string.IsNullOrWhiteSpace(model.RequestedInTime) && string.IsNullOrWhiteSpace(model.RequestedOutTime))
                return ServiceResult<ReviewRequestViewModel>.Fail("Provide at least one corrected time (In or Out).");

            var pendingTiming = await _requestRepository.GetByStudentAsync(student.Id);
            if (pendingTiming.Any(r =>
                    r.Status == RequestStatus.Pending &&
                    r.FieldName.Equals(AttendanceTimingField, StringComparison.OrdinalIgnoreCase) &&
                    r.OldValue.Equals(dateKey, StringComparison.OrdinalIgnoreCase)))
                return ServiceResult<ReviewRequestViewModel>.Fail("A pending timing request already exists for this date.");

            var timingPayload = $"In={model.RequestedInTime?.Trim() ?? ""}|Out={model.RequestedOutTime?.Trim() ?? ""}|Reason={model.Reason.Trim()}";
            var timingRequest = new ProfileUpdateRequest
            {
                StudentId = student.Id,
                StudentUserId = student.UserId,
                StudentName = student.Name,
                FieldName = AttendanceTimingField,
                OldValue = dateKey,
                NewValue = timingPayload,
                Status = RequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _requestRepository.AddAsync(timingRequest);
            await _activityService.LogAsync(
                student.UserId,
                student.Name,
                "Attendance Timing Regularization",
                $"{student.Name} requested timing update for {date:dd MMM yyyy}: {model.Reason.Trim()}");

            return ServiceResult<ReviewRequestViewModel>.Ok(MapToViewModel(timingRequest), "Timing regularization request submitted.");
        }

        if (record.Status == AttendanceStatus.Present || record.Status == AttendanceStatus.Late)
            return ServiceResult<ReviewRequestViewModel>.Fail("You are already marked Present for that day.");

        var pending = await _requestRepository.GetByStudentAsync(student.Id);
        if (pending.Any(r =>
                r.Status == RequestStatus.Pending &&
                r.FieldName.Equals(AttendanceField, StringComparison.OrdinalIgnoreCase) &&
                r.OldValue.Equals(dateKey, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<ReviewRequestViewModel>.Fail("A pending attendance request already exists for this date.");

        var request = new ProfileUpdateRequest
        {
            StudentId = student.Id,
            StudentUserId = student.UserId,
            StudentName = student.Name,
            FieldName = AttendanceField,
            OldValue = dateKey,
            NewValue = model.Reason.Trim(),
            Status = RequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _requestRepository.AddAsync(request);

        await _activityService.LogAsync(
            student.UserId,
            student.Name,
            "Attendance Correction Requested",
            $"{student.Name} requested Present for {date:dd MMM yyyy}: {model.Reason.Trim()}");

        return ServiceResult<ReviewRequestViewModel>.Ok(MapToViewModel(request), "Attendance correction request submitted to admin.");
    }

    public async Task<IReadOnlyList<ReviewRequestViewModel>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        var requests = await _requestRepository.GetByStatusAsync(RequestStatus.Pending);
        return requests
            .OrderByDescending(r => r.CreatedAt)
            .Select(MapToViewModel)
            .ToList();
    }

    public async Task<IReadOnlyList<ReviewRequestViewModel>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var requests = await _requestRepository.GetAllAsync();
        return requests
            .Where(r => r.Status != RequestStatus.Pending)
            .OrderByDescending(r => r.ReviewedAt ?? r.CreatedAt)
            .Select(MapToViewModel)
            .ToList();
    }

    public async Task<IReadOnlyList<ReviewRequestViewModel>> GetByStudentAsync(string studentId, CancellationToken cancellationToken = default)
    {
        var requests = await _requestRepository.GetByStudentAsync(studentId);
        return requests
            .OrderByDescending(r => r.CreatedAt)
            .Select(MapToViewModel)
            .ToList();
    }

    public async Task<ServiceResult<ReviewRequestViewModel>> ApproveAsync(
        string id,
        string? remarks,
        string reviewedBy,
        string reviewerName,
        CancellationToken cancellationToken = default)
    {
        var request = await _requestRepository.GetByIdAsync(id);
        if (request is null)
            return ServiceResult<ReviewRequestViewModel>.Fail("Request not found.");

        if (request.Status != RequestStatus.Pending)
            return ServiceResult<ReviewRequestViewModel>.Fail("Only pending requests can be approved.");

        var student = await _studentRepository.GetByIdAsync(request.StudentId);
        if (student is null)
            return ServiceResult<ReviewRequestViewModel>.Fail("Student not found.");

        if (IsAttendanceRequest(request.FieldName))
        {
            var apply = await ApplyAttendancePresentAsync(student, request, remarks, reviewedBy);
            if (!apply.Success)
                return ServiceResult<ReviewRequestViewModel>.Fail(apply.Message ?? "Unable to update attendance.");
        }
        else if (IsAttendanceTimingRequest(request.FieldName))
        {
            var apply = await ApplyAttendanceTimingAsync(student, request, remarks, reviewedBy);
            if (!apply.Success)
                return ServiceResult<ReviewRequestViewModel>.Fail(apply.Message ?? "Unable to update timing.");
        }
        else
        {
            if (!TryApplyFieldUpdate(student, request.FieldName, request.NewValue, out var error))
                return ServiceResult<ReviewRequestViewModel>.Fail(error);

            student.UpdatedAt = DateTime.UtcNow;
            await _studentRepository.UpdateAsync(student);

            if (string.Equals(NormalizeFieldName(request.FieldName), "Name", StringComparison.OrdinalIgnoreCase))
            {
                var user = await _userRepository.GetByIdAsync(student.UserId);
                if (user is not null)
                {
                    user.FullName = student.Name;
                    user.UpdatedAt = DateTime.UtcNow;
                    await _userRepository.UpdateAsync(user);
                }
            }
        }

        request.Status = RequestStatus.Approved;
        request.AdminRemarks = remarks?.Trim();
        request.ReviewedBy = reviewedBy;
        request.ReviewedAt = DateTime.UtcNow;
        await _requestRepository.UpdateAsync(request);

        if (IsAttendanceRequest(request.FieldName))
        {
            await _notificationService.CreateAsync(
                request.StudentUserId,
                "Attendance Request Approved",
                $"Your attendance on {FormatDateKey(request.OldValue)} was marked Present (Manual).",
                NotificationType.AttendanceMarked,
                "/Student/AttendanceHistory",
                cancellationToken);

            await _activityService.LogAsync(
                reviewedBy,
                reviewerName,
                "Attendance Request Approved",
                $"Approved Present for {request.StudentName} on {FormatDateKey(request.OldValue)}.");
        }
        else if (IsAttendanceTimingRequest(request.FieldName))
        {
            await _notificationService.CreateAsync(
                request.StudentUserId,
                "Timing Regularization Approved",
                $"Your attendance timing request for {FormatDateKey(request.OldValue)} was approved.",
                NotificationType.AttendanceMarked,
                "/Student/AttendanceHistory",
                cancellationToken);

            await _activityService.LogAsync(
                reviewedBy,
                reviewerName,
                "Timing Regularization Approved",
                $"Approved timing update for {request.StudentName} on {FormatDateKey(request.OldValue)}.");
        }
        else
        {
            await _notificationService.CreateAsync(
                request.StudentUserId,
                "Profile Update Approved",
                $"Your request to update {request.FieldName} has been approved.",
                NotificationType.ProfileApproved,
                "/Student/Profile",
                cancellationToken);

            await _activityService.LogAsync(
                reviewedBy,
                reviewerName,
                "Profile Request Approved",
                $"Approved {request.FieldName} update for {request.StudentName}.");
        }

        return ServiceResult<ReviewRequestViewModel>.Ok(MapToViewModel(request), "Request approved.");
    }

    public async Task<ServiceResult<ReviewRequestViewModel>> RejectAsync(
        string id,
        string? remarks,
        string reviewedBy,
        string reviewerName,
        CancellationToken cancellationToken = default)
    {
        var request = await _requestRepository.GetByIdAsync(id);
        if (request is null)
            return ServiceResult<ReviewRequestViewModel>.Fail("Request not found.");

        if (request.Status != RequestStatus.Pending)
            return ServiceResult<ReviewRequestViewModel>.Fail("Only pending requests can be rejected.");

        request.Status = RequestStatus.Rejected;
        request.AdminRemarks = remarks?.Trim();
        request.ReviewedBy = reviewedBy;
        request.ReviewedAt = DateTime.UtcNow;
        await _requestRepository.UpdateAsync(request);

        if (IsAttendanceRequest(request.FieldName))
        {
            await _notificationService.CreateAsync(
                request.StudentUserId,
                "Attendance Request Rejected",
                $"Your attendance request for {FormatDateKey(request.OldValue)} was rejected.{(string.IsNullOrWhiteSpace(remarks) ? string.Empty : $" Remarks: {remarks.Trim()}")}",
                NotificationType.General,
                "/Student/AttendanceHistory",
                cancellationToken);

            await _activityService.LogAsync(
                reviewedBy,
                reviewerName,
                "Attendance Request Rejected",
                $"Rejected attendance request for {request.StudentName} on {FormatDateKey(request.OldValue)}.");
        }
        else
        {
            await _notificationService.CreateAsync(
                request.StudentUserId,
                "Profile Update Rejected",
                $"Your request to update {request.FieldName} has been rejected.{(string.IsNullOrWhiteSpace(remarks) ? string.Empty : $" Remarks: {remarks.Trim()}")}",
                NotificationType.ProfileRejected,
                "/Student/Profile",
                cancellationToken);

            await _activityService.LogAsync(
                reviewedBy,
                reviewerName,
                "Profile Request Rejected",
                $"Rejected {request.FieldName} update for {request.StudentName}.");
        }

        return ServiceResult<ReviewRequestViewModel>.Ok(MapToViewModel(request), "Request rejected.");
    }

    private async Task<ServiceResult> ApplyAttendancePresentAsync(
        Student student,
        ProfileUpdateRequest request,
        string? adminRemarks,
        string reviewedBy)
    {
        if (!DateTime.TryParse(request.OldValue, out var date))
            return ServiceResult.Fail("Invalid attendance date on request.");

        var targetDate = date.Date;
        var existing = await _attendanceRepository.GetByDateAsync(targetDate);
        var record = existing.FirstOrDefault(r => r.StudentId == student.Id);

        var remarkParts = new List<string> { $"Student reason: {request.NewValue}" };
        if (!string.IsNullOrWhiteSpace(adminRemarks))
            remarkParts.Add($"Admin: {adminRemarks.Trim()}");
        var remarks = string.Join(" | ", remarkParts);

        if (record is null)
        {
            record = new AttendanceRecord
            {
                StudentId = student.Id,
                StudentCode = student.StudentId,
                StudentName = student.Name,
                Department = student.Department,
                Course = student.Course,
                Date = targetDate,
                Status = AttendanceStatus.Present,
                MarkedBy = reviewedBy,
                Remarks = remarks,
                Source = "Manual",
                CreatedAt = DateTime.UtcNow
            };
            await _attendanceRepository.AddAsync(record);
        }
        else
        {
            record.Status = AttendanceStatus.Present;
            record.Source = "Manual";
            record.MarkedBy = reviewedBy;
            record.StudentName = student.Name;
            record.Remarks = remarks;
            await _attendanceRepository.UpdateAsync(record);
        }

        return ServiceResult.Ok("Attendance marked Present.");
    }

    private async Task<ServiceResult> ApplyAttendanceTimingAsync(
        Student student,
        ProfileUpdateRequest request,
        string? adminRemarks,
        string reviewedBy)
    {
        if (!DateTime.TryParse(request.OldValue, out var date))
            return ServiceResult.Fail("Invalid attendance date on request.");

        var existing = await _attendanceRepository.GetByDateAsync(date.Date);
        var record = existing.FirstOrDefault(r => r.StudentId == student.Id);
        if (record is null)
            return ServiceResult.Fail("Attendance record not found.");

        string? requestedIn = null;
        string? requestedOut = null;
        var parts = request.NewValue.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("In=", StringComparison.OrdinalIgnoreCase))
                requestedIn = part[3..].Trim();
            else if (part.StartsWith("Out=", StringComparison.OrdinalIgnoreCase))
                requestedOut = part[4..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(requestedIn))
            record.FirstIn = requestedIn;
        if (!string.IsNullOrWhiteSpace(requestedOut))
            record.LastOut = requestedOut;

        var remarkParts = new List<string> { $"Timing regularization: {request.NewValue}" };
        if (!string.IsNullOrWhiteSpace(adminRemarks))
            remarkParts.Add($"Admin: {adminRemarks.Trim()}");
        record.Remarks = string.Join(" | ", remarkParts);
        record.MarkedBy = reviewedBy;
        record.Source = "Manual";
        await _attendanceRepository.UpdateAsync(record);
        return ServiceResult.Ok("Attendance timing updated.");
    }

    private static bool IsAttendanceRequest(string fieldName) =>
        fieldName.Equals(AttendanceField, StringComparison.OrdinalIgnoreCase);

    private static bool IsAttendanceTimingRequest(string fieldName) =>
        fieldName.Equals(AttendanceTimingField, StringComparison.OrdinalIgnoreCase);

    private static string FormatDateKey(string dateKey) =>
        DateTime.TryParse(dateKey, out var d) ? d.ToString("dd MMM yyyy") : dateKey;

    private static string NormalizeFieldName(string fieldName)
    {
        return fieldName.Trim() switch
        {
            "DOB" => "DateOfBirth",
            _ => fieldName.Trim()
        };
    }

    private static string GetFieldValue(Student student, string fieldName) =>
        fieldName switch
        {
            "Name" => student.Name,
            "Department" => student.Department,
            "Semester" => student.Semester,
            "Course" => student.Course,
            "EnrollmentNumber" => student.EnrollmentNumber,
            "DateOfBirth" => student.DateOfBirth.ToString("yyyy-MM-dd"),
            "Gender" => student.Gender.ToString(),
            _ => string.Empty
        };

    private static bool TryApplyFieldUpdate(Student student, string fieldName, string newValue, out string error)
    {
        error = string.Empty;

        switch (fieldName)
        {
            case "Name":
                if (string.IsNullOrWhiteSpace(newValue))
                {
                    error = "Name cannot be empty.";
                    return false;
                }
                student.Name = newValue.Trim();
                return true;

            case "Department":
                if (string.IsNullOrWhiteSpace(newValue))
                {
                    error = "Department cannot be empty.";
                    return false;
                }
                student.Department = newValue.Trim();
                return true;

            case "Semester":
                if (string.IsNullOrWhiteSpace(newValue))
                {
                    error = "Semester cannot be empty.";
                    return false;
                }
                student.Semester = newValue.Trim();
                return true;

            case "Course":
                if (string.IsNullOrWhiteSpace(newValue))
                {
                    error = "Course cannot be empty.";
                    return false;
                }
                student.Course = newValue.Trim();
                return true;

            case "EnrollmentNumber":
                if (string.IsNullOrWhiteSpace(newValue))
                {
                    error = "Enrollment number cannot be empty.";
                    return false;
                }
                student.EnrollmentNumber = newValue.Trim();
                return true;

            case "DateOfBirth":
                if (!DateTime.TryParse(newValue, out var dob))
                {
                    error = "Invalid date of birth.";
                    return false;
                }
                student.DateOfBirth = dob.Date;
                return true;

            case "Gender":
                if (!Enum.TryParse<Gender>(newValue, ignoreCase: true, out var gender))
                {
                    error = "Invalid gender value.";
                    return false;
                }
                student.Gender = gender;
                return true;

            default:
                error = "Unsupported field.";
                return false;
        }
    }

    private static ReviewRequestViewModel MapToViewModel(ProfileUpdateRequest request) => new()
    {
        Id = request.Id,
        StudentName = request.StudentName,
        FieldName = request.FieldName,
        OldValue = request.OldValue,
        NewValue = request.NewValue,
        Status = request.Status.ToString(),
        AdminRemarks = request.AdminRemarks
    };
}
