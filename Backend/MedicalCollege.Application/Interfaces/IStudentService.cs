using MedicalCollege.Application.Common;
using MedicalCollege.Application.ViewModels;

namespace MedicalCollege.Application.Interfaces;

public interface IStudentService
{
    Task<PagedResult<StudentFormViewModel>> SearchAsync(StudentListFilter filter, CancellationToken cancellationToken = default);
    Task<StudentFormViewModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<StudentFormViewModel?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<StudentProfileViewModel?> GetProfileAsync(string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<StudentFormViewModel>> CreateStudentAsync(
        StudentFormViewModel model,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<StudentImportResultViewModel>> ImportFromCsvAsync(
        Stream csvStream,
        string? defaultClassId,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<StudentFormViewModel>> UpdateStudentAsync(
        StudentFormViewModel model,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default);
    Task<ServiceResult> DeactivateStudentAsync(
        string id,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default);
    Task<ServiceResult> DeleteStudentAsync(
        string id,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<string>> ResetStudentPasswordAsync(
        string id,
        string newPassword,
        string actorUserId,
        string actorName,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<StudentProfileViewModel>> UpdateEditableProfileAsync(
        string userId,
        StudentProfileViewModel model,
        CancellationToken cancellationToken = default);
    Task<ServiceResult> MarkFaceEnrolledAsync(string studentId, int? frmStudentId, CancellationToken cancellationToken = default);
    Task<string> GenerateNextStudentIdAsync(string course, string department, CancellationToken cancellationToken = default);
    int CalculateProfileCompletionPercent(StudentProfileViewModel profile);
    IReadOnlyList<string> GetProtectedFieldNames();
}
