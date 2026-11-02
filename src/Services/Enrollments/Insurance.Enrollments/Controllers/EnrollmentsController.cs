using System.Security.Claims;
using Insurance.BuildingBlocks.Security;
using Insurance.Enrollments.Contracts;
using Insurance.Enrollments.DTOs;
using Insurance.Enrollments.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.Enrollments.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EnrollmentsController : ControllerBase
{
    private readonly IEnrollmentService _enrollments;
    public EnrollmentsController(IEnrollmentService enrollments) => _enrollments = enrollments;

    // Caller's ID comes from the validated token, 
    // not from the request body or URL.
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("mine")]
    public async Task<ActionResult<List<EnrollmentDto>>> GetMine()
    {
        var list = await _enrollments.GetMineAsync(CurrentUserId);
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(EnrollmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnrollmentDto>> GetById(int id)
    {
        var dto = await _enrollments.GetByIdAsync(id, CurrentUserId, User.IsInRole(nameof(UserRole.Admin)));
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // Errors (400 bad date/expired quote, 403 email not verified,
    // 404 quote not found, 409 duplicate/quote already used)
    // are translated by ApiExceptionHandler.
    // 201 once the enrollment saga has finished; 202 if it is still running after the wait (rare).
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Customer))]      // Only Customers buy policies
    [ProducesResponseType(typeof(EnrollmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(EnrollmentDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EnrollmentDto>> Enroll(EnrollmentRequest request)
    {
        var dto = await _enrollments.EnrollAsync(CurrentUserId, request);
        if (dto.Status == EnrollmentStatus.Pending)
            return AcceptedAtAction(nameof(GetById), new { id = dto.EnrollmentId }, dto);
        return CreatedAtAction(nameof(GetById), new { id = dto.EnrollmentId }, dto);
    }

    [HttpPut("{id:int}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(int id)
    {
        var cancelled = await _enrollments.CancelAsync(id, CurrentUserId);
        if (!cancelled) return NotFound();
        return NoContent();
    }
}