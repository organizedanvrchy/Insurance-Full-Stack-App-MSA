using System.Net;
using System.Net.Http.Json;
using Insurance.BuildingBlocks.Http;

namespace Insurance.Claims.Contracts;

/// <summary>
/// A claim's documents for other services (Claims' internal API): today, the Notifications service attaches copies
/// to the customer's "We received your claim" email. Only callers with a service token are allowed.
/// </summary>
public interface IClaimDocuments
{
    Task<List<ClaimDocumentInfo>> GetForClaimAsync(int claimId);

    /// <summary>The file's bytes, or null if it no longer exists.</summary>
    Task<byte[]?> GetContentAsync(int claimId, int claimDocumentId);
}

public record ClaimDocumentInfo(int ClaimDocumentId, int ClaimId, string FileName, string ContentType, int SizeBytes, DateTime UploadedAt);

/// <summary>Calls /internal/claims/{claimId}/documents on the Claims service.</summary>
public class ClaimDocumentsClient : IClaimDocuments
{
    private readonly HttpClient _http;
    public ClaimDocumentsClient(HttpClient http) => _http = http;

    public async Task<List<ClaimDocumentInfo>> GetForClaimAsync(int claimId)
    {
        var response = await _http.GetAsync($"internal/claims/{claimId}/documents");
        return await response.ReadOrThrowAsync<List<ClaimDocumentInfo>>();
    }

    public async Task<byte[]?> GetContentAsync(int claimId, int claimDocumentId)
    {
        var response = await _http.GetAsync($"internal/claims/{claimId}/documents/{claimDocumentId}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await response.ThrowIfFailedAsync();
        return await response.Content.ReadAsByteArrayAsync();
    }
}
