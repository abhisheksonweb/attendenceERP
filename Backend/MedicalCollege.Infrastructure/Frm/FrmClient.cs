using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedicalCollege.Infrastructure.Frm;

public interface IFrmClient
{
    Task<FrmSyncResult?> SyncClassAsync(FrmSyncRequest request, CancellationToken ct = default);
    Task<string?> GetRecognizeUrlAsync(string externalClassId, CancellationToken ct = default);
    Task<FrmCaptureResult?> CaptureFaceAsync(int frmClassId, CancellationToken ct = default);
    Task<FrmEnrollResult?> EnrollFaceAsync(int frmClassId, FrmEnrollRequest request, CancellationToken ct = default);
    Task<FrmEnrollResult?> EnrollFromPhotoUrlAsync(int frmClassId, FrmEnrollFromUrlRequest request, CancellationToken ct = default);
    Task<FrmEnrollResult?> EnrollFromPhotoFileAsync(int frmClassId, FrmEnrollFromFileRequest request, Stream photoStream, string fileName, string? contentType, CancellationToken ct = default);
    Task<IReadOnlyList<FrmAttendanceRow>?> GetClassAttendanceAsync(int frmClassId, CancellationToken ct = default);
    Task<IReadOnlyList<FrmSessionRow>?> GetClassSessionsAsync(int frmClassId, CancellationToken ct = default);
    Task<bool> DeleteStudentAsync(int frmStudentId, CancellationToken ct = default);
    string GetPreviewFeedUrl();
    string BaseUrl { get; }
}

public class FrmSyncRequest
{
    [JsonPropertyName("external_id")]
    public string ExternalId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("students")]
    public List<FrmSyncStudent> Students { get; set; } = new();
}

public class FrmSyncStudent
{
    [JsonPropertyName("external_id")]
    public string ExternalId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("roll_no")]
    public string RollNo { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
}

public class FrmSyncResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("frm_class_id")]
    public int FrmClassId { get; set; }

    [JsonPropertyName("students")]
    public List<FrmStudentMap>? Students { get; set; }
}

public class FrmStudentMap
{
    [JsonPropertyName("external_id")]
    public string ExternalId { get; set; } = string.Empty;

    [JsonPropertyName("frm_student_id")]
    public int FrmStudentId { get; set; }
}

public class FrmRecognizeUrlResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class FrmCaptureResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("samples")]
    public int Samples { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("duplicate")]
    public FrmDuplicateInfo? Duplicate { get; set; }
}

public class FrmDuplicateInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("roll_no")]
    public string? RollNo { get; set; }

    [JsonPropertyName("same_class")]
    public bool SameClass { get; set; }

    [JsonPropertyName("score")]
    public double Score { get; set; }
}

public class FrmEnrollRequest
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("roll_no")]
    public string RollNo { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("external_id")]
    public string ExternalId { get; set; } = string.Empty;
}

public class FrmEnrollFromUrlRequest
{
    [JsonPropertyName("photo_url")]
    public string PhotoUrl { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("roll_no")]
    public string RollNo { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("external_id")]
    public string ExternalId { get; set; } = string.Empty;
}

public class FrmEnrollFromFileRequest
{
    public string Name { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string ExternalId { get; set; } = string.Empty;
}

public class FrmEnrollResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("frm_student_id")]
    public int FrmStudentId { get; set; }
}

public class FrmAttendanceRow
{
    [JsonPropertyName("student_id")]
    public int StudentId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("roll_no")]
    public string RollNo { get; set; } = string.Empty;

    [JsonPropertyName("sessions")]
    public int Sessions { get; set; }

    [JsonPropertyName("first_in")]
    public string? FirstIn { get; set; }

    [JsonPropertyName("last_out")]
    public string? LastOut { get; set; }

    [JsonPropertyName("time_in_class")]
    public string? TimeInClass { get; set; }

    [JsonPropertyName("time_in_class_seconds")]
    public int TimeInClassSeconds { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "OUT";

    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }
}

public class FrmSessionRow
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("roll_no")]
    public string RollNo { get; set; } = string.Empty;

    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("entry_time")]
    public string? EntryTime { get; set; }

    [JsonPropertyName("exit_time")]
    public string? ExitTime { get; set; }

    [JsonPropertyName("entry_ts")]
    public string? EntryTs { get; set; }

    [JsonPropertyName("exit_ts")]
    public string? ExitTs { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "OUT";
}

public class FrmClient : IFrmClient
{
    private readonly HttpClient _http;
    private readonly ILogger<FrmClient> _logger;
    private readonly string _apiKey;

    public string BaseUrl { get; }

    public FrmClient(HttpClient http, IConfiguration config, ILogger<FrmClient> logger)
    {
        _http = http;
        _logger = logger;
        BaseUrl = config["Frm:BaseUrl"]?.TrimEnd('/') ?? "http://127.0.0.1:8000";
        _apiKey = config["Frm:ApiKey"] ?? "medcollege-frm-key";
        _http.BaseAddress = new Uri(BaseUrl + "/");
        _http.DefaultRequestHeaders.Remove("X-Api-Key");
        _http.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
        _http.Timeout = TimeSpan.FromSeconds(120);
    }

    public string GetPreviewFeedUrl()
        => $"{BaseUrl}/preview_feed?api_key={Uri.EscapeDataString(_apiKey)}";

    public async Task<FrmSyncResult?> SyncClassAsync(FrmSyncRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/v1/sync/class", request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("FRM sync failed: {Status} {Body}", response.StatusCode, body);
                return null;
            }

            return JsonSerializer.Deserialize<FrmSyncResult>(body, Options());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FRM sync unavailable");
            return null;
        }
    }

    public async Task<string?> GetRecognizeUrlAsync(string externalClassId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"api/v1/classes/{Uri.EscapeDataString(externalClassId)}/recognize-url", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("FRM recognize-url failed: {Status} {Body}", response.StatusCode, body);
                return null;
            }

            var result = JsonSerializer.Deserialize<FrmRecognizeUrlResult>(body, Options());
            if (result?.Url is null) return null;
            return result.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? result.Url
                : BaseUrl.TrimEnd('/') + result.Url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FRM recognize-url unavailable");
            return null;
        }
    }

    public async Task<FrmCaptureResult?> CaptureFaceAsync(int frmClassId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync($"classes/{frmClassId}/capture", null, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<FrmCaptureResult>(body, Options());
            if (result is null)
                return new FrmCaptureResult { Ok = false, Error = "Invalid response from FRModule." };
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FRM capture failed");
            return new FrmCaptureResult { Ok = false, Error = "Face module unavailable. Is FRModule running?" };
        }
    }

    public async Task<FrmEnrollResult?> EnrollFaceAsync(int frmClassId, FrmEnrollRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"classes/{frmClassId}/students", request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<FrmEnrollResult>(body, Options());
            if (result is null)
                return new FrmEnrollResult { Ok = false, Error = "Invalid response from FRModule." };
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FRM enroll failed");
            return new FrmEnrollResult { Ok = false, Error = "Face module unavailable. Is FRModule running?" };
        }
    }

    public async Task<FrmEnrollResult?> EnrollFromPhotoUrlAsync(int frmClassId, FrmEnrollFromUrlRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"api/v1/classes/{frmClassId}/enroll-from-url", request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<FrmEnrollResult>(body, Options());
            if (result is null)
                return new FrmEnrollResult { Ok = false, Error = "Invalid response from FRModule." };
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FRM enroll-from-url failed");
            return new FrmEnrollResult { Ok = false, Error = "Face module unavailable. Is FRModule running?" };
        }
    }

    public async Task<FrmEnrollResult?> EnrollFromPhotoFileAsync(
        int frmClassId,
        FrmEnrollFromFileRequest request,
        Stream photoStream,
        string fileName,
        string? contentType,
        CancellationToken ct = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var streamContent = new StreamContent(photoStream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
            form.Add(streamContent, "photo", string.IsNullOrWhiteSpace(fileName) ? "face.jpg" : fileName);
            form.Add(new StringContent(request.Name), "name");
            form.Add(new StringContent(request.RollNo), "roll_no");
            form.Add(new StringContent(request.Email ?? ""), "email");
            form.Add(new StringContent(request.Phone ?? ""), "phone");
            form.Add(new StringContent(request.ExternalId), "external_id");

            var response = await _http.PostAsync($"api/v1/classes/{frmClassId}/enroll-from-file", form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<FrmEnrollResult>(body, Options());
            if (result is null)
                return new FrmEnrollResult { Ok = false, Error = "Invalid response from FRModule." };
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FRM enroll-from-file failed");
            return new FrmEnrollResult { Ok = false, Error = "Face module unavailable. Is FRModule running?" };
        }
    }

    public async Task<IReadOnlyList<FrmAttendanceRow>?> GetClassAttendanceAsync(int frmClassId, CancellationToken ct = default)
    {
        try
        {
            // api_key query is a fallback so poll works even if header is stripped.
            var response = await _http.GetAsync(
                $"classes/{frmClassId}/api/attendance?api_key={Uri.EscapeDataString(_apiKey)}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<List<FrmAttendanceRow>>(body, Options());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FRM attendance poll failed");
            return null;
        }
    }

    public async Task<IReadOnlyList<FrmSessionRow>?> GetClassSessionsAsync(int frmClassId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync(
                $"classes/{frmClassId}/api/sessions?api_key={Uri.EscapeDataString(_apiKey)}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<List<FrmSessionRow>>(body, Options());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FRM sessions poll failed");
            return null;
        }
    }

    public async Task<bool> DeleteStudentAsync(int frmStudentId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/v1/students/{frmStudentId}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FRM delete student {Id} failed", frmStudentId);
            return false;
        }
    }

    private static JsonSerializerOptions Options() => new() { PropertyNameCaseInsensitive = true };
}
