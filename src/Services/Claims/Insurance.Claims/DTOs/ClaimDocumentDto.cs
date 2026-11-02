namespace Insurance.Claims.DTOs;

// StorageKey stays on the server: clients reach a file only through the download endpoint.
public class ClaimDocumentDto
{
    public int ClaimDocumentId { get; set; }
    public int ClaimId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}
