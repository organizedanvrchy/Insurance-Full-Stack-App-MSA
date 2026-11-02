using Insurance.BuildingBlocks.Exceptions;
using Insurance.Claims.Contracts;
using Insurance.Claims.Exceptions;
using Insurance.Claims.Models;
using Insurance.Claims.Services;
using Xunit;

namespace Insurance.Claims.Tests;

public class ClaimDocumentServiceTests
{
    private const int Owner = 1;
    private const int OtherCustomer = 3;
    private const int Admin = 7;

    private static readonly byte[] Pdf = "%PDF-1.7 test"u8.ToArray();
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };

    private readonly FakeClaimRepository _claims = new();
    private readonly FakeClaimDocumentRepository _documents = new();
    private readonly FakeDocumentStorage _storage = new();
    private readonly ClaimDocumentService _sut;

    public ClaimDocumentServiceTests() =>
        _sut = new ClaimDocumentService(_claims, _documents, _storage);

    private InsuranceClaim AddClaim(ClaimStatus status = ClaimStatus.Submitted)
    {
        var claim = new InsuranceClaim { ClaimId = _claims.Claims.Count + 1, UserId = Owner, Status = status };
        _claims.Claims.Add(claim);
        return claim;
    }

    private Task<DTOs.ClaimDocumentDto> Upload(int claimId, byte[] bytes, string name = "estimate.pdf", int userId = Owner) =>
        _sut.UploadAsync(claimId, userId, name, bytes.Length, new MemoryStream(bytes));

    // Upload

    [Theory]
    [MemberData(nameof(AllowedFiles))]
    public async Task The_type_comes_from_the_files_contents(byte[] bytes, string contentType, string extension)
    {
        var claim = AddClaim();

        var dto = await Upload(claim.ClaimId, bytes, "misleading-name.txt");

        Assert.Equal(contentType, dto.ContentType);
        Assert.EndsWith(extension, _documents.Documents.Single().StorageKey);
    }

    public static TheoryData<byte[], string, string> AllowedFiles => new()
    {
        { Pdf, "application/pdf", ".pdf" },
        { Jpeg, "image/jpeg", ".jpg" },
        { Png, "image/png", ".png" },
    };

    [Fact]
    public async Task The_whole_file_is_stored_under_a_generated_key()
    {
        var claim = AddClaim();

        await Upload(claim.ClaimId, Pdf, "../../secrets.pdf");

        var document = _documents.Documents.Single();
        Assert.Equal(Pdf, _storage.Files[document.StorageKey]);
        Assert.DoesNotContain("secrets", document.StorageKey);
        Assert.Equal("secrets.pdf", document.FileName);
        Assert.Equal(Owner, document.UploadedByUserId);
    }

    [Fact]
    public async Task A_file_pretending_to_be_a_pdf_is_refused()
    {
        var claim = AddClaim();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Upload(claim.ClaimId, "<script>alert(1)</script>"u8.ToArray(), "invoice.pdf"));

        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        var claim = AddClaim();

        await Assert.ThrowsAsync<BusinessRuleException>(() => Upload(claim.ClaimId, Array.Empty<byte>()));
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused()
    {
        var claim = AddClaim();

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.UploadAsync(
            claim.ClaimId, Owner, "big.pdf", ClaimDocumentService.MaxFileBytes + 1L, new MemoryStream(Pdf)));
    }

    [Fact]
    public async Task A_very_long_file_name_keeps_its_extension()
    {
        var claim = AddClaim();

        var dto = await Upload(claim.ClaimId, Pdf, new string('a', 400) + ".pdf");

        Assert.Equal(255, dto.FileName.Length);
        Assert.EndsWith(".pdf", dto.FileName);
    }

    [Theory]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Paid)]
    public async Task A_decided_claim_takes_no_more_documents(ClaimStatus status)
    {
        var claim = AddClaim(status);

        await Assert.ThrowsAsync<ClaimConflictException>(() => Upload(claim.ClaimId, Pdf));
    }

    [Fact]
    public async Task A_customer_cant_upload_to_someone_elses_claim()
    {
        var claim = AddClaim();

        await Assert.ThrowsAsync<NotFoundException>(() => Upload(claim.ClaimId, Pdf, userId: OtherCustomer));

        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task The_sixth_document_is_refused_and_its_file_is_removed()
    {
        var claim = AddClaim();
        for (var i = 0; i < ClaimDocumentService.MaxDocumentsPerClaim; i++)
            await Upload(claim.ClaimId, Pdf);

        await Assert.ThrowsAsync<ClaimConflictException>(() => Upload(claim.ClaimId, Pdf));

        Assert.Equal(ClaimDocumentService.MaxDocumentsPerClaim, _storage.Files.Count);
    }

    [Fact]
    public async Task If_the_database_fails_the_saved_file_is_removed()
    {
        var claim = AddClaim();
        _documents.FailNextAdd = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Upload(claim.ClaimId, Pdf));

        Assert.Empty(_storage.Files);
    }

    // List and download

    [Theory]
    [InlineData(Owner)]
    [InlineData(Admin)]
    public async Task The_owner_and_admins_can_list_and_download(int userId)
    {
        var claim = AddClaim();
        var uploaded = await Upload(claim.ClaimId, Pdf, "estimate.pdf");
        var isAdmin = userId == Admin;

        var list = await _sut.GetForClaimAsync(claim.ClaimId, userId, isAdmin);
        var download = await _sut.DownloadAsync(claim.ClaimId, uploaded.ClaimDocumentId, userId, isAdmin);

        Assert.Equal("estimate.pdf", Assert.Single(list).FileName);
        Assert.Equal("estimate.pdf", download.FileName);
        Assert.Equal("application/pdf", download.ContentType);

        using var content = new MemoryStream();
        await download.Content.CopyToAsync(content);
        Assert.Equal(Pdf, content.ToArray());
    }

    [Fact]
    public async Task Another_customer_cant_see_a_claims_documents()
    {
        var claim = AddClaim();
        var uploaded = await Upload(claim.ClaimId, Pdf);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetForClaimAsync(claim.ClaimId, OtherCustomer, isAdmin: false));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.DownloadAsync(claim.ClaimId, uploaded.ClaimDocumentId, OtherCustomer, isAdmin: false));
    }

    [Fact]
    public async Task A_document_is_only_reachable_through_its_own_claim()
    {
        var first = AddClaim();
        var second = AddClaim();
        var uploaded = await Upload(first.ClaimId, Pdf);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.DownloadAsync(second.ClaimId, uploaded.ClaimDocumentId, Owner, isAdmin: false));
    }

    [Fact]
    public async Task A_row_whose_file_is_missing_is_a_404()
    {
        var claim = AddClaim();
        var uploaded = await Upload(claim.ClaimId, Pdf);
        _storage.Files.Clear();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.DownloadAsync(claim.ClaimId, uploaded.ClaimDocumentId, Owner, isAdmin: false));
    }
}
