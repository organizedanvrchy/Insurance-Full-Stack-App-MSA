using Insurance.BuildingBlocks.Exceptions;
using Insurance.Identity.Contracts;
using Insurance.Identity.DTOs;
using Insurance.Identity.Messaging;
using Insurance.Identity.Models;
using Insurance.Identity.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Insurance.Identity.Services;

// Proving that a customer owns their email address, at sign-up and after they change it.
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
        var user = await GetActiveUserAsync(userId);

        if (user.EmailVerified)
            throw new ConflictException("Your email address is already verified.");

        await EnforceLimitAsync(user.UserId);
        await IssueLinkAsync(user);
    }

    public async Task<UserDto> ChangeEmailAsync(int userId, ChangeEmailRequest request)
    {
        var user = await GetActiveUserAsync(userId);

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw new BusinessRuleException("Your current password is incorrect.");

        var newEmail = request.NewEmail.Trim().ToLowerInvariant();
        if (newEmail == user.Email)
            throw new BusinessRuleException("That is already your email address.");

        // Each change emails a link, so it counts toward the daily cap. Not the cooldown: that stops repeat emails to
        // the SAME inbox, and someone who mistyped their address at sign-up should be able to fix it straight away.
        await EnforceLimitAsync(user.UserId, cooldown: false);

        var oldEmail = await _users.ChangeEmailAsync(user.UserId, newEmail);   // 409 if the address is taken
        user.Email = newEmail;
        user.EmailVerifiedAt = null;

        // The old address is told (it's the owner's way to notice a takeover), and the new one must be verified.
        await TryPublishAsync(new EmailChanged(user.UserId, user.FullName, oldEmail, newEmail, _clock.GetUtcNow().UtcDateTime));
        try
        {
            await IssueLinkAsync(user, afterEmailChange: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send the verification email after user {UserId} changed their email.", user.UserId);
        }

        return AuthService.MapToDto(user);
    }

    private async Task<User> GetActiveUserAsync(int userId)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null || !user.IsActive)
            throw new NotFoundException("Account not found.");
        return user;
    }

    // For notices: the change itself has already happened, so failing to announce it is only logged.
    private async Task TryPublishAsync<T>(T message) where T : class
    {
        try
        {
            await _events.PublishAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not publish {Event}.", typeof(T).Name);
        }
    }

    // Stops a script (or an impatient double click) from flooding someone's inbox. The cooldown only applies while
    // a link sent moments ago is still waiting to be used; the daily cap counts every link.
    private async Task EnforceLimitAsync(int userId, bool cooldown = true)
    {
        var recent = await _tokens.GetRecentAsync(userId);
        var now = _clock.GetUtcNow().UtcDateTime;

        if (cooldown && recent.LastPendingCreatedAtUtc is { } last && now - last < _options.ResendCooldown)
        {
            var wait = (int)Math.Ceiling((_options.ResendCooldown - (now - last)).TotalSeconds);
            throw new TooManyRequestsException($"We just sent you a link. Please wait {wait} seconds before asking for another email.");
        }

        if (recent.CountLastDay >= _options.MaxEmailsPerDay)
            throw new TooManyRequestsException("You've asked for too many verification emails today. Please try again tomorrow.");
    }

    private async Task IssueLinkAsync(User user, bool afterEmailChange = false)
    {
        var token = VerificationTokens.NewToken();
        var expires = _clock.GetUtcNow().UtcDateTime.Add(_options.LinkLifetime);

        await _tokens.CreateAsync(user.UserId, user.Email, VerificationTokens.Hash(token), expires);
        await _events.PublishAsync(new EmailVerificationRequested(user.UserId, user.FullName, user.Email, token, expires, afterEmailChange));
    }
}
