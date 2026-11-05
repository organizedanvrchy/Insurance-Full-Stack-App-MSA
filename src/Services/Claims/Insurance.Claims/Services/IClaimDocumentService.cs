using Insurance.Claims.DTOs;

namespace Insurance.Claims.Services;

public interface IClaimDocumentService
{
    // content must be seekable: its first bytes are read to detect the file type.
    Task<ClaimDocumentDto> UploadAsync(int claimId, int userId, string fileName, long length, Stream content);
    Task<List<ClaimDocumentDto>> GetForClaimAsync(int claimId, int userId, bool isAdmin);
    Task<ClaimDocumentDownload> DownloadAsync(int claimId, int claimDocumentId, int userId, bool isAdmin);
}

// The caller disposes Content.
public record ClaimDocumentDownload(Stream Content, string ContentType, string FileName);
