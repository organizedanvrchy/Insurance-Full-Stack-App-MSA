using System.Security.Claims;
using Insurance.BuildingBlocks.Security;
using Insurance.Claims.DTOs;
using Insurance.Claims.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.Claims.Controllers;

[ApiController]
[Route("api/claims/{claimId:int}/documents")]
[Authorize]
public class ClaimDocumentsController : ControllerBase
{
    // A little over the 10 MB file limit, so a slightly large file gets the service's friendly message.
    private const long MaxRequestBytes = ClaimDocumentService.MaxFileBytes + 1024 * 1024;

    private readonly IClaimDocumentService _documents;
    public ClaimDocumentsController(IClaimDocumentService documents) => _documents = documents;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole(nameof(UserRole.Admin));

    // Customer: add one file (multipart/form-data, field "file") to their own claim.
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Customer))]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType(typeof(ClaimDocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimDocumentDto>> Upload(int claimId, IFormFile file)
    {
        await using var content = file.OpenReadStream();
        var dto = await _documents.UploadAsync(claimId, CurrentUserId, file.FileName, file.Length, content);
        return CreatedAtAction(nameof(Download), new { claimId, documentId = dto.ClaimDocumentId }, dto);
    }

    // Owner or admin
    [HttpGet]
    public async Task<ActionResult<List<ClaimDocumentDto>>> GetAll(int claimId)
    {
        return Ok(await _documents.GetForClaimAsync(claimId, CurrentUserId, IsAdmin));
    }

    // Owner or admin. Always a download, never shown inline, so an uploaded file can't run in our origin.
    [HttpGet("{documentId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(int claimId, int documentId)
    {
        var download = await _documents.DownloadAsync(claimId, documentId, CurrentUserId, IsAdmin);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(download.Content, download.ContentType, download.FileName);   // disposes the stream when sent
    }
}
