using Insurance.BuildingBlocks.Exceptions;
using Insurance.Identity.Contracts;
using Insurance.Identity.DTOs;
using Insurance.Identity.Messaging;
using Insurance.Identity.Models;
using Insurance.Identity.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Insurance.Identity.Services;

// Proving that a customer owns their email address, at sign-up and whenever they change it.
//   1. A random token is created; its hash is stored, the token itself goes out in EmailVerificationRequested.
//   2. The Notifications service emails the link (<app>/verify-email?token=...).
//   3. The customer opens it; the app posts the token to /api/auth/verify-email, which uses it up.
// Until step 3, a new customer can sign in and look around, but the Enrollments and Claims services refuse to
// enroll them or take a claim. A changed address only replaces the current one at step 3.
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

    public async Task SendRegistrationLinkAsync(User user)
    {
        try
        {
            await IssueLinkAsync(user, user.Email, EmailVerificationPurpose.Registration);
        }
        catch (Exception ex)
        {
            // The account exists either way; the customer can press "Resend" once the bus is back.
            _logger.LogWarning(ex, "Could not send the verification email for new user {UserId}.", user.UserId);
        }
    }

    public async Task<VerifyEmailResponse> VerifyAsync(string token)
    {
        var used = await _tokens.ConsumeAsync(VerificationTokens.Hash(token));
        var user = used.User;
        var changed = used.Purpose == EmailVerificationPurpose.EmailChange;

        if (changed)
            await TryPublishAsync(new EmailChanged(user.UserId, user.FullName, used.PreviousEmail, user.Email));

        return new VerifyEmailResponse { Email = user.Email, EmailChanged = changed };
    }

    public async Task ResendAsync(int userId)
    {
        var user = await GetActiveUserAsync(userId);

        if (user.PendingEmail is { } pending)
        {
            await EnforceLimitAsync(user.UserId);
            await IssueLinkAsync(user, pending, EmailVerificationPurpose.EmailChange);
            return;
        }

        if (user.EmailVerified)
            throw new ConflictException("Your email address is already verified.");

        await EnforceLimitAsync(user.UserId);
        await IssueLinkAsync(user, user.Email, EmailVerificationPurpose.Registration);
    }

    public async Task<UserDto> RequestEmailChangeAsync(int userId, ChangeEmailRequest request)
    {
        var user = await GetActiveUserAsync(userId);

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw new BusinessRuleException("Your current password is incorrect.");

        var newEmail = request.NewEmail.Trim().ToLowerInvariant();
        if (newEmail == user.Email)
            throw new BusinessRuleException("That is already your email address.");

        await EnforceLimitAsync(user.UserId);

        // Throws DuplicateEmailException (409) if another account already uses the address.
        await _users.SetPendingEmailAsync(user.UserId, newEmail);
        user.PendingEmail = newEmail;

        await IssueLinkAsync(user, newEmail, EmailVerificationPurpose.EmailChange);

        // A heads-up to the address the account uses now, in case this wasn't its owner.
        await TryPublishAsync(new EmailChangeRequested(user.UserId, user.FullName, user.Email, newEmail));

        return AuthService.MapToDto(user);
    }

    public async Task<UserDto> CancelEmailChangeAsync(int userId)
    {
        var user = await GetActiveUserAsync(userId);
        if (user.PendingEmail is not null)
        {
            await _users.SetPendingEmailAsync(user.UserId, null);   // also stops the emailed link from working
            user.PendingEmail = null;
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

    // Stops a script (or an impatient double click) from flooding someone's inbox.
    private async Task EnforceLimitAsync(int userId)
    {
        var recent = await _tokens.GetRecentAsync(userId);
        var now = _clock.GetUtcNow().UtcDateTime;

        if (recent.LastCreatedAtUtc is { } last && now - last < _options.ResendCooldown)
        {
            var wait = (int)Math.Ceiling((_options.ResendCooldown - (now - last)).TotalSeconds);
            throw new TooManyRequestsException($"We just sent you an email. Please wait {wait} seconds before asking for another.");
        }

        if (recent.CountLastDay >= _options.MaxEmailsPerDay)
            throw new TooManyRequestsException("You've asked for too many verification emails today. Please try again tomorrow.");
    }

    private async Task IssueLinkAsync(User user, string email, EmailVerificationPurpose purpose)
    {
        var token = VerificationTokens.NewToken();
        var expires = _clock.GetUtcNow().UtcDateTime.Add(_options.LinkLifetime);

        await _tokens.CreateAsync(user.UserId, email, purpose, VerificationTokens.Hash(token), expires);
        await _events.PublishAsync(new EmailVerificationRequested(user.UserId, user.FullName, email, token, purpose, expires));
    }

    // For courtesy notices: the change itself has already happened, so a failure to announce it is only logged.
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
}
