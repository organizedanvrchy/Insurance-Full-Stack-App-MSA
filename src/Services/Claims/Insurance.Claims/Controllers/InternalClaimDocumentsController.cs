using Insurance.BuildingBlocks.Security;
using Insurance.Claims.Contracts;
using Insurance.Claims.Repositories;
using Insurance.Claims.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.Claims.Controllers;

// A claim's documents for other services (the Notifications service emails copies to the customer).
// Service tokens only: a customer's or admin's token is refused here even inside the network, and the gateway
// never routes /internal. Customers and admins use /api/claims/{id}/documents, which checks ownership.
[ApiController]
[Route("internal/claims/{claimId:int}/documents")]
[Authorize(Roles = ServiceTokenProvider.Role)]
public class InternalClaimDocumentsController : ControllerBase
{
    private readonly IClaimDocumentRepository _documents;
    private readonly IDocumentStorage _storage;

    public InternalClaimDocumentsController(IClaimDocumentRepository documents, IDocumentStorage storage)
    {
        _documents = documents;
        _storage = storage;
    }

    [HttpGet]
    public async Task<ActionResult<List<ClaimDocumentInfo>>> GetAll(int claimId) =>
        Ok((await _documents.GetByClaimAsync(claimId))
            .Select(d => new ClaimDocumentInfo(d.ClaimDocumentId, d.ClaimId, d.FileName, d.ContentType, d.SizeBytes,
                DateTime.SpecifyKind(d.UploadedAt, DateTimeKind.Utc)))
            .ToList());

    [HttpGet("{documentId:int}")]
    public async Task<IActionResult> GetContent(int claimId, int documentId)
    {
        var document = await _documents.GetByIdAsync(claimId, documentId);
        var content = document is null ? null : await _storage.OpenReadAsync(document.StorageKey);
        if (document is null || content is null) return NotFound();
        return File(content, document.ContentType);
    }
}
