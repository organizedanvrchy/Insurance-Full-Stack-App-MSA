using System.Security.Claims;
using Insurance.BuildingBlocks.Security;
using Insurance.Notifications.DTOs;
using Insurance.Notifications.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.Notifications.Controllers;

// A customer's email settings, and the emails we've sent them. Admins get no customer emails, so customers only.
[ApiController]
[Route("api/notifications")]
[Authorize(Roles = nameof(UserRole.Customer))]
public class NotificationsController : ControllerBase
{
    private readonly INotificationRepository _repo;
    public NotificationsController(INotificationRepository repo) => _repo = repo;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("preferences")]
    public async Task<ActionResult<PreferencesDto>> GetPreferences()
    {
        var p = await _repo.GetPreferencesAsync(CurrentUserId);
        return Ok(new PreferencesDto { PaymentReminders = p.PaymentReminders });
    }

    // Only payment reminders can be turned off; receipts, claim updates and account emails always go out.
    [HttpPut("preferences")]
    [ProducesResponseType(typeof(PreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PreferencesDto>> UpdatePreferences(UpdatePreferencesRequest request)
    {
        var p = await _repo.SetPreferencesAsync(CurrentUserId, request.PaymentReminders!.Value);
        return Ok(new PreferencesDto { PaymentReminders = p.PaymentReminders });
    }

    // GET /api/notifications/history?take=10 -> the most recent emails sent to the caller
    [HttpGet("history")]
    public async Task<ActionResult<List<SentEmailDto>>> GetHistory([FromQuery] int take = 10)
    {
        var emails = await _repo.GetRecentAsync(CurrentUserId, Math.Clamp(take, 1, 50));
        return Ok(emails.Select(e => new SentEmailDto
        {
            EmailId = e.EmailId,
            Kind = e.Kind,
            Subject = e.Subject,
            ToEmail = e.ToEmail,
            SentAt = e.SentAtUtc
        }).ToList());
    }
}
