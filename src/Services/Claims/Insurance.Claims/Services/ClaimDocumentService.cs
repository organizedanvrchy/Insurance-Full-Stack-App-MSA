using Insurance.BuildingBlocks.Exceptions;
using Insurance.Claims.DTOs;
using Insurance.Claims.Exceptions;
using Insurance.Claims.Models;
using Insurance.Claims.Repositories;

namespace Insurance.Claims.Services;

public class ClaimDocumentService : IClaimDocumentService
{
    public const int MaxDocumentsPerClaim = 5;
    public const int MaxFileBytes = 10 * 1024 * 1024;   // 10 MB

    private const int MaxFileNameLength = 255;

    // The type is decided by the file's first bytes. The name and the browser's content type can say anything.
    private static readonly (byte[] Signature, string ContentType, string Extension)[] AllowedTypes =
    {
        ("%PDF-"u8.ToArray(), "application/pdf", ".pdf"),
        (new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg", ".jpg"),
        (new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png", ".png"),
    };

    private readonly IClaimRepository _claims;
    private readonly IClaimDocumentRepository _documents;
    private readonly IDocumentStorage _storage;

    public ClaimDocumentService(IClaimRepository claims, IClaimDocumentRepository documents, IDocumentStorage storage)
    {
        _claims = claims;
        _documents = documents;
        _storage = storage;
    }

    public async Task<ClaimDocumentDto> UploadAsync(int claimId, int userId, string fileName, long length, Stream content)
    {
        // Only the customer who owns the claim can add to it.
        var claim = await _claims.GetByIdAsync(claimId);
        if (claim is null || claim.UserId != userId)
            throw new NotFoundException("Claim not found.");

        if (!ClaimStatusTransitions.AcceptsDocuments(claim.Status))
            throw new ClaimConflictException("Documents can't be added to a claim once a decision has been made.");

        if (length <= 0)
            throw new BusinessRuleException("The file is empty.");
        if (length > MaxFileBytes)
            throw new BusinessRuleException("Each file can be up to 10 MB.");

        var type = await DetectTypeAsync(content)
            ?? throw new BusinessRuleException("Please upload a PDF, JPG or PNG file.");

        var document = new ClaimDocument
        {
            ClaimId = claimId,
            FileName = CleanFileName(fileName, type.Extension),
            ContentType = type.ContentType,
            SizeBytes = (int)length,
            StorageKey = $"{Guid.NewGuid():N}{type.Extension}",
            UploadedByUserId = userId
        };

        // File first, then the row: a row must never point at a file that isn't there.
        await _storage.SaveAsync(document.StorageKey, content);
        try
        {
            return ToDto(await _documents.AddAsync(document, MaxDocumentsPerClaim));
        }
        catch
        {
            await _storage.DeleteAsync(document.StorageKey);
            throw;
        }
    }

    public async Task<List<ClaimDocumentDto>> GetForClaimAsync(int claimId, int userId, bool isAdmin)
    {
        await EnsureCanViewAsync(claimId, userId, isAdmin);
        return (await _documents.GetByClaimAsync(claimId)).Select(ToDto).ToList();
    }

    public async Task<ClaimDocumentDownload> DownloadAsync(int claimId, int claimDocumentId, int userId, bool isAdmin)
    {
        await EnsureCanViewAsync(claimId, userId, isAdmin);

        var document = await _documents.GetByIdAsync(claimId, claimDocumentId)
            ?? throw new NotFoundException("Document not found.");

        var content = await _storage.OpenReadAsync(document.StorageKey)
            ?? throw new NotFoundException("Document not found.");

        return new ClaimDocumentDownload(content, document.ContentType, document.FileName);
    }

    // Same answer for "doesn't exist" and "belongs to someone else", so ids can't be probed.
    private async Task EnsureCanViewAsync(int claimId, int userId, bool isAdmin)
    {
        var claim = await _claims.GetByIdAsync(claimId);
        if (claim is null || (claim.UserId != userId && !isAdmin))
            throw new NotFoundException("Claim not found.");
    }

    private static async Task<(byte[] Signature, string ContentType, string Extension)?> DetectTypeAsync(Stream content)
    {
        var header = new byte[AllowedTypes.Max(t => t.Signature.Length)];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        content.Position = 0;

        return MatchType(new ReadOnlySpan<byte>(header, 0, read));
    }

    private static (byte[] Signature, string ContentType, string Extension)? MatchType(ReadOnlySpan<byte> header)
    {
        foreach (var type in AllowedTypes)
        {
            if (header.StartsWith(new ReadOnlySpan<byte>(type.Signature)))
                return type;
        }
        return null;
    }

    // Display only (the file is stored under StorageKey), but it does end up in a download's headers.
    private static string CleanFileName(string fileName, string extension)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (string.IsNullOrEmpty(name)) return $"document{extension}";
        if (name.Length <= MaxFileNameLength) return name;

        return name[..(MaxFileNameLength - extension.Length)] + extension;
    }

    private static ClaimDocumentDto ToDto(ClaimDocument d) => new()
    {
        ClaimDocumentId = d.ClaimDocumentId,
        ClaimId = d.ClaimId,
        FileName = d.FileName,
        ContentType = d.ContentType,
        SizeBytes = d.SizeBytes,
        UploadedAt = d.UploadedAt
    };
}
