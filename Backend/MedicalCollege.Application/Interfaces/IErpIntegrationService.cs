using MedicalCollege.Application.Common;
using MedicalCollege.Domain.Entities;

namespace MedicalCollege.Application.Interfaces;

/// <summary>
/// College ERP push stub — on-prem by default; enable when ERP endpoint is ready.
/// </summary>
public interface IErpIntegrationService
{
    Task<ServiceResult> PushAttendanceAsync(AttendanceRecord record, Student student, CancellationToken ct = default);
}
