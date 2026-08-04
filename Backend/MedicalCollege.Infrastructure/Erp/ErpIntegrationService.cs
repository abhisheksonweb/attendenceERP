using System.Net.Http.Json;
using MedicalCollege.Application.Common;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedicalCollege.Infrastructure.Erp;

public class ErpIntegrationService : IErpIntegrationService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ErpIntegrationService> _logger;

    public ErpIntegrationService(HttpClient http, IConfiguration config, ILogger<ErpIntegrationService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<ServiceResult> PushAttendanceAsync(AttendanceRecord record, Student student, CancellationToken ct = default)
    {
        var enabled = string.Equals(_config["Erp:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled)
            return ServiceResult.Ok("ERP push disabled.");

        var baseUrl = (_config["Erp:BaseUrl"] ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return ServiceResult.Fail("ERP BaseUrl not configured.");

        try
        {
            var payload = new
            {
                studentCode = student.StudentId,
                enrollmentNumber = student.EnrollmentNumber,
                studentName = student.Name,
                date = record.Date.ToString("yyyy-MM-dd"),
                status = record.Status.ToString(),
                source = record.Source,
                firstIn = record.FirstIn,
                lastOut = record.LastOut,
                earlyLeave = record.EarlyLeave,
                syncedAt = DateTime.UtcNow
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/attendance/sync")
            {
                Content = JsonContent.Create(payload)
            };
            var apiKey = _config["Erp:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ERP push failed: {Status}", response.StatusCode);
                return ServiceResult.Fail($"ERP returned {(int)response.StatusCode}");
            }

            return ServiceResult.Ok("Pushed to ERP.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ERP push skipped");
            return ServiceResult.Fail("ERP unreachable.");
        }
    }
}
