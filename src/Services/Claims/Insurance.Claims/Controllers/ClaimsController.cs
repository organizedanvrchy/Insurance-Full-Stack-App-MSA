using System.Security.Claims;
using Insurance.BuildingBlocks.Security;
using Insurance.Claims.DTOs;
using Insurance.Claims.Enums;
using Insurance.Claims.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.Claims.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ClaimsController : ControllerBase
{
    private readonly IClaimService _claims;
    public ClaimsController(IClaimService claims) => _claims = claims;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Customer: file a claim. Errors (400/404/409) are translated by ApiExceptionHandler.
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Customer))]
    [ProducesResponseType(typeof(ClaimDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimDto>> Submit(ClaimSubmitRequest request)
    {
        var dto = await _claims.SubmitAsync(CurrentUserId, request);
        return CreatedAtAction(nameof(GetById), new { id = dto.ClaimId }, dto);
    }

    // Customer: my claims
    [HttpGet("mine")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    public async Task<ActionResult<List<ClaimDto>>> GetMine()
    {
        var list = await _claims.GetMineAsync(CurrentUserId);
        return Ok(list);
    }

    // Admin: every claim, optionally filtered, e.g. GET /api/claims?status=Submitted
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<List<ClaimDto>>> GetAll([FromQuery] ClaimStatus? status)
    {
        var list = await _claims.GetAllAsync(status);
        return Ok(list);
    }

    // Owner or admin
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDto>> GetById(int id)
    {
        var dto = await _claims.GetByIdAsync(id, CurrentUserId, User.IsInRole(nameof(UserRole.Admin)));
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // Admin: move a claim to its next status
    [HttpPut("{id:int}/review")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(ClaimDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimDto>> Review(int id, ClaimReviewRequest request)
    {
        var dto = await _claims.ReviewAsync(id, CurrentUserId, request);
        return Ok(dto);
    }

    // Customer: send a claim back to the reviewer after a request for more information
    [HttpPost("{id:int}/send-to-reviewer")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    [ProducesResponseType(typeof(ClaimDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimDto>> SendToReviewer(int id)
    {
        var dto = await _claims.SendToReviewerAsync(id, CurrentUserId);
        return Ok(dto);
    }
}