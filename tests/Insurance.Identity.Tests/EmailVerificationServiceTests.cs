using Insurance.BuildingBlocks.Exceptions;
using Insurance.BuildingBlocks.Security;
using Insurance.Identity.Contracts;
using Insurance.Identity.DTOs;
using Insurance.Identity.Exceptions;
using Insurance.Identity.Models;
using Insurance.Identity.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Insurance.Identity.Tests;

public class EmailVerificationServiceTests
{
    private const string Password = "Str0ng!Pass";

    private readonly ManualClock _clock = new();
    private readonly FakeUserRepository _users = new();
    private readonly FakeEmailTokenRepository _tokens;
    private readonly FakeIdentityEvents _events = new();
    private readonly EmailVerificationService _sut;
    private readonly AuthService _auth;

    public EmailVerificationServiceTests()
    {
        _tokens = new FakeEmailTokenRepository(_users, _clock);
        _sut = new EmailVerificationService(_users, _tokens, _events, Options.Create(new EmailVerificationOptions()),
            _clock, NullLogger<EmailVerificationService>.Instance);
        _auth = new AuthService(_users, new FakeTokenService(), _sut);
    }

    private async Task<User> RegisterAsync(string email = "jane.doe@example.com")
    {
        await _auth.RegisterAsync(new RegisterRequest
        {
            FirstName = "Jane", LastName = "Doe", Email = email, Password = Password,
            Street = "48 Maple Avenue", City = "Stamford", State = "CT", PostalCode = "06902"
        });
        return _users.Users.Single(u => u.Email == email);
    }

    private string LastToken => _events.OfType<EmailVerificationRequested>().Last().Token;

    // ---------------- Registration ----------------

    [Fact]
    public async Task A_new_account_starts_unverified_and_is_sent_a_link()
    {
        var response = await _auth.RegisterAsync(new RegisterRequest
        {
            FirstName = "Jane", LastName = "Doe", Email = "Jane.Doe@Example.com", Password = Password,
            Street = "48 Maple Avenue", City = "Stamford", State = "CT", PostalCode = "06902"
        });

        Assert.False(response.User.EmailVerified);
        var sent = Assert.Single(_events.OfType<EmailVerificationRequested>());
        Assert.Equal("jane.doe@example.com", sent.Email);
        Assert.Equal(EmailVerificationPurpose.Registration, sent.Purpose);
        Assert.Equal(_clock.Now.UtcDateTime.AddHours(24), sent.ExpiresAtUtc);
    }

    [Fact]
    public async Task Only_a_hash_of_the_token_is_stored()
    {
        await RegisterAsync();

        var row = Assert.Single(_tokens.Rows);
        Assert.NotEqual(LastToken, row.Hash);
        Assert.Equal(VerificationTokens.Hash(LastToken), row.Hash);
        Assert.Matches("^[0-9a-f]{64}$", row.Hash);
    }

    [Fact]
    public void Tokens_are_long_random_and_url_safe()
    {
        var a = VerificationTokens.NewToken();
        var b = VerificationTokens.NewToken();

        Assert.NotEqual(a, b);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);      // 32 bytes, base64url without padding
    }

    [Fact]
    public async Task Registration_still_succeeds_when_the_email_cannot_be_queued()
    {
        _events.Fail = true;

        var user = await RegisterAsync();

        Assert.False(user.EmailVerified);     // the customer can press "Resend" later
    }

    // ---------------- Verifying ----------------

    [Fact]
    public async Task The_link_verifies_the_address()
    {
        var user = await RegisterAsync();

        var result = await _sut.VerifyAsync(LastToken);

        Assert.True(user.EmailVerified);
        Assert.False(result.EmailChanged);
        Assert.Equal("jane.doe@example.com", result.Email);
        Assert.True((await _auth.GetProfileAsync(user.UserId))!.EmailVerified);
    }

    [Fact]
    public async Task A_link_works_only_once()
    {
        await RegisterAsync();
        var token = LastToken;
        await _sut.VerifyAsync(token);

        await Assert.ThrowsAsync<InvalidVerificationLinkException>(() => _sut.VerifyAsync(token));
    }

    [Fact]
    public async Task An_expired_link_is_refused()
    {
        var user = await RegisterAsync();
        _clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidVerificationLinkException>(() => _sut.VerifyAsync(LastToken));
        Assert.False(user.EmailVerified);
    }

    [Fact]
    public async Task An_unknown_token_is_refused_as_a_400()
    {
        var ex = await Assert.ThrowsAsync<InvalidVerificationLinkException>(() => _sut.VerifyAsync("not-a-real-token"));
        Assert.IsAssignableFrom<BusinessRuleException>(ex);
    }

    [Fact]
    public async Task A_newer_link_replaces_the_older_one()
    {
        var user = await RegisterAsync();
        var first = LastToken;
        _clock.Advance(TimeSpan.FromMinutes(2));
        await _sut.ResendAsync(user.UserId);

        await Assert.ThrowsAsync<InvalidVerificationLinkException>(() => _sut.VerifyAsync(first));
        await _sut.VerifyAsync(LastToken);
        Assert.True(user.EmailVerified);
    }

    // ---------------- Resending ----------------

    [Fact]
    public async Task Resending_for_a_verified_address_is_a_conflict()
    {
        var user = await RegisterAsync();
        await _sut.VerifyAsync(LastToken);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ResendAsync(user.UserId));
    }

    [Fact]
    public async Task Resending_straight_away_is_rate_limited()
    {
        var user = await RegisterAsync();
        _clock.Advance(TimeSpan.FromSeconds(20));

        var ex = await Assert.ThrowsAsync<TooManyRequestsException>(() => _sut.ResendAsync(user.UserId));
        Assert.Contains("40 seconds", ex.Message);
    }

    [Fact]
    public async Task At_most_six_links_are_sent_in_a_day()
    {
        var user = await RegisterAsync();               // 1
        for (var i = 0; i < 5; i++)                     // 2..6
        {
            _clock.Advance(TimeSpan.FromMinutes(2));
            await _sut.ResendAsync(user.UserId);
        }
        _clock.Advance(TimeSpan.FromMinutes(2));

        await Assert.ThrowsAsync<TooManyRequestsException>(() => _sut.ResendAsync(user.UserId));

        _clock.Advance(TimeSpan.FromHours(24));         // a day later it works again
        await _sut.ResendAsync(user.UserId);
        Assert.Equal(7, _events.OfType<EmailVerificationRequested>().Count());
    }

    // ---------------- Changing the email address ----------------

    private async Task<User> VerifiedUserAsync()
    {
        var user = await RegisterAsync();
        await _sut.VerifyAsync(LastToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        return user;
    }

    private static ChangeEmailRequest Change(string email, string password = Password) =>
        new() { NewEmail = email, CurrentPassword = password };

    [Fact]
    public async Task Changing_email_needs_the_current_password()
    {
        var user = await VerifiedUserAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.RequestEmailChangeAsync(user.UserId, Change("new@example.com", "wrong-password")));
        Assert.Null(user.PendingEmail);
    }

    [Fact]
    public async Task Changing_to_the_same_address_is_refused()
    {
        var user = await VerifiedUserAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.RequestEmailChangeAsync(user.UserId, Change(" JANE.DOE@example.com ")));
    }

    [Fact]
    public async Task Changing_to_an_address_another_account_uses_is_a_conflict()
    {
        var user = await VerifiedUserAsync();
        await RegisterAsync("taken@example.com");

        await Assert.ThrowsAsync<DuplicateEmailException>(() =>
            _sut.RequestEmailChangeAsync(user.UserId, Change("taken@example.com")));
    }

    [Fact]
    public async Task A_change_waits_for_the_new_address_to_be_confirmed()
    {
        var user = await VerifiedUserAsync();

        var dto = await _sut.RequestEmailChangeAsync(user.UserId, Change("New.Address@Example.com"));

        Assert.Equal("jane.doe@example.com", user.Email);          // still signs in with the old one
        Assert.Equal("new.address@example.com", dto.PendingEmail);

        var link = _events.OfType<EmailVerificationRequested>().Last();
        Assert.Equal("new.address@example.com", link.Email);
        Assert.Equal(EmailVerificationPurpose.EmailChange, link.Purpose);

        var notice = Assert.Single(_events.OfType<EmailChangeRequested>());
        Assert.Equal("jane.doe@example.com", notice.CurrentEmail);   // the heads-up goes to the current address
        Assert.Equal("new.address@example.com", notice.NewEmail);
    }

    [Fact]
    public async Task Confirming_the_new_address_switches_the_account_to_it()
    {
        var user = await VerifiedUserAsync();
        await _sut.RequestEmailChangeAsync(user.UserId, Change("new@example.com"));

        var result = await _sut.VerifyAsync(LastToken);

        Assert.True(result.EmailChanged);
        Assert.Equal("new@example.com", user.Email);
        Assert.Null(user.PendingEmail);
        Assert.True(user.EmailVerified);

        var changed = Assert.Single(_events.OfType<EmailChanged>());
        Assert.Equal("jane.doe@example.com", changed.OldEmail);
        Assert.Equal("new@example.com", changed.NewEmail);

        Assert.NotNull(await _auth.LoginAsync(new LoginRequest { Email = "new@example.com", Password = Password }));
        Assert.Null(await _auth.LoginAsync(new LoginRequest { Email = "jane.doe@example.com", Password = Password }));
    }

    [Fact]
    public async Task Cancelling_a_change_makes_its_link_useless()
    {
        var user = await VerifiedUserAsync();
        await _sut.RequestEmailChangeAsync(user.UserId, Change("new@example.com"));
        var token = LastToken;

        var dto = await _sut.CancelEmailChangeAsync(user.UserId);

        Assert.Null(dto.PendingEmail);
        await Assert.ThrowsAsync<InvalidVerificationLinkException>(() => _sut.VerifyAsync(token));
        Assert.Equal("jane.doe@example.com", user.Email);
    }

    [Fact]
    public async Task Resending_while_a_change_is_pending_sends_the_link_to_the_new_address()
    {
        var user = await VerifiedUserAsync();
        await _sut.RequestEmailChangeAsync(user.UserId, Change("new@example.com"));
        _clock.Advance(TimeSpan.FromMinutes(2));

        await _sut.ResendAsync(user.UserId);

        var link = _events.OfType<EmailVerificationRequested>().Last();
        Assert.Equal("new@example.com", link.Email);
        Assert.Equal(EmailVerificationPurpose.EmailChange, link.Purpose);
    }

    [Fact]
    public async Task A_deactivated_account_cannot_change_its_email()
    {
        var user = await VerifiedUserAsync();
        user.IsActive = false;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.RequestEmailChangeAsync(user.UserId, Change("new@example.com")));
    }

    [Fact]
    public async Task The_user_directory_reports_whether_the_email_is_verified()
    {
        var user = await RegisterAsync();
        var directory = new UserDirectory(_users);

        Assert.False((await directory.GetAsync(user.UserId))!.EmailVerified);
        await _sut.VerifyAsync(LastToken);
        Assert.True((await directory.GetAsync(user.UserId))!.EmailVerified);
        Assert.Equal(UserRole.Customer, (await directory.GetAsync(user.UserId))!.Role);
    }
}
