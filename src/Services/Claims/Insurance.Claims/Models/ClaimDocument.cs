namespace Insurance.Claims.Models;

// The details of a file attached to a claim. The file itself is in document storage, under StorageKey.
public class ClaimDocument
{
    public int ClaimDocumentId { get; set; }
    public int ClaimId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public int UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }
}
