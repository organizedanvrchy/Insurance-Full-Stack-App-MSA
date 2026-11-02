using Insurance.BuildingBlocks.Exceptions;
using Insurance.Identity.Contracts;
using Insurance.Identity.DTOs;
using Insurance.Identity.Messaging;
using Insurance.Identity.Models;
using Insurance.Identity.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Insurance.Identity.Services;

// Proving that a customer owns their email address, at sign-up.
//   1. A random token is created; its hash is stored, the token itself goes out in EmailVerificationRequested.
//   2. The Notifications service emails the link (<app>/verify-email?token=...).
//   3. The customer opens it; the app posts the token to /api/auth/verify-email, which uses it up.
// Until step 3, a new customer can sign in and look around, but the Enrollments and Claims services refuse to
// enroll them or take a claim.
public class EmailVerificationService : IEmailVerificationService
{
    private readonly IUserRepository _users;
    private readonly IEmailTokenRepository _tokens;
    private readonly IIdentityEvents _events;
    private readonly EmailVerificationOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(IUserRepository users, IEmailTokenRepository tokens, IIdentityEvents events,
        IOptions<EmailVerificationOptions> options, TimeProvider clock, ILogger<EmailVerificationService> logger)
    {
        _users = users;
        _tokens = tokens;
        _events = events;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task OnRegisteredAsync(User user, bool paymentReminders)
    {
        // The account exists either way; nothing here may fail the sign-up.
        try
        {
            await _events.PublishAsync(new CustomerRegistered(user.UserId, paymentReminders));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record the email preferences of new user {UserId}.", user.UserId);
        }

        try
        {
            await IssueLinkAsync(user);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send the verification email for new user {UserId}.", user.UserId);
        }
    }

    public async Task<VerifyEmailResponse> VerifyAsync(string token)
    {
        var user = await _tokens.ConsumeAsync(VerificationTokens.Hash(token));
        return new VerifyEmailResponse { Email = user.Email };
    }

    public async Task ResendAsync(int userId)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
            throw new NotFoundException("Account not found.");

        if (user.EmailVerified)
            throw new ConflictException("Your email address is already verified.");

        await EnforceLimitAsync(user.UserId);
        await IssueLinkAsync(user);
    }

    // Stops a script (or an impatient double click) from flooding someone's inbox. The cooldown only applies while
    // a link sent moments ago is still waiting to be used; the daily cap counts every link.
    private async Task EnforceLimitAsync(int userId)
    {
        var recent = await _tokens.GetRecentAsync(userId);
        var now = _clock.GetUtcNow().UtcDateTime;

        if (recent.LastPendingCreatedAtUtc is { } last && now - last < _options.ResendCooldown)
        {
            var wait = (int)Math.Ceiling((_options.ResendCooldown - (now - last)).TotalSeconds);
            throw new TooManyRequestsException($"We just sent you a link. Please wait {wait} seconds before asking for another email.");
        }

        if (recent.CountLastDay >= _options.MaxEmailsPerDay)
            throw new TooManyRequestsException("You've asked for too many verification emails today. Please try again tomorrow.");
    }

    private async Task IssueLinkAsync(User user)
    {
        var token = VerificationTokens.NewToken();
        var expires = _clock.GetUtcNow().UtcDateTime.Add(_options.LinkLifetime);

        await _tokens.CreateAsync(user.UserId, user.Email, VerificationTokens.Hash(token), expires);
        await _events.PublishAsync(new EmailVerificationRequested(user.UserId, user.FullName, user.Email, token, expires));
    }
}
