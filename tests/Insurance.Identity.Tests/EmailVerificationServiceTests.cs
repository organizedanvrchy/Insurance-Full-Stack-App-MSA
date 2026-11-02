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
        _auth = new AuthService(_users, new FakeTokenService(), _sut, _events, NullLogger<AuthService>.Instance);
    }

    private async Task<User> RegisterAsync(string email = "jane.doe@example.com", bool? paymentReminders = null)
    {
        await _auth.RegisterAsync(new RegisterRequest
        {
            FirstName = "Jane", LastName = "Doe", Email = email, Password = Password,
            Street = "48 Maple Avenue", City = "Stamford", State = "CT", PostalCode = "06902",
            PaymentReminders = paymentReminders
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
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(15), sent.ExpiresAtUtc);
        Assert.Equal(15, sent.ValidForMinutes);
    }

    [Theory]
    [InlineData(null, true)]      // not answered: reminders on
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task The_sign_up_email_choice_is_passed_on(bool? answer, bool expected)
    {
        var user = await RegisterAsync(paymentReminders: answer);

        var registered = Assert.Single(_events.OfType<CustomerRegistered>());
        Assert.Equal(new CustomerRegistered(user.UserId, expected), registered);
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
        _clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

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
    public async Task At_most_ten_links_are_sent_in_a_day()
    {
        var user = await RegisterAsync();               // 1
        for (var i = 0; i < 9; i++)                     // 2..10
        {
            _clock.Advance(TimeSpan.FromMinutes(2));
            await _sut.ResendAsync(user.UserId);
        }
        _clock.Advance(TimeSpan.FromMinutes(2));

        await Assert.ThrowsAsync<TooManyRequestsException>(() => _sut.ResendAsync(user.UserId));

        _clock.Advance(TimeSpan.FromHours(24));         // a day later it works again
        await _sut.ResendAsync(user.UserId);
        Assert.Equal(11, _events.OfType<EmailVerificationRequested>().Count());
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

    // ---------------- Changing the email ----------------

    private async Task<User> VerifiedUserAsync()
    {
        var user = await RegisterAsync();
        await _sut.VerifyAsync(LastToken);
        return user;
    }

    private static ChangeEmailRequest Change(string email) => new() { NewEmail = email };

    [Theory]
    [InlineData("", false)]
    [InlineData("not-an-email", false)]
    [InlineData("a@b", false)]                 // no domain ending: undeliverable
    [InlineData("new@example.com", true)]
    public void Changing_email_only_needs_a_valid_address(string email, bool valid)
    {
        var request = new ChangeEmailRequest { NewEmail = email };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var ok = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            request, new System.ComponentModel.DataAnnotations.ValidationContext(request), results, validateAllProperties: true);

        Assert.Equal(valid, ok);
    }

    [Fact]
    public async Task Changing_to_the_same_address_is_refused()
    {
        var user = await VerifiedUserAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ChangeEmailAsync(user.UserId, Change(" JANE.DOE@example.com ")));
    }

    [Fact]
    public async Task Changing_to_an_address_another_account_uses_is_a_conflict()
    {
        var user = await VerifiedUserAsync();
        await RegisterAsync("taken@example.com");

        await Assert.ThrowsAsync<DuplicateEmailException>(() => _sut.ChangeEmailAsync(user.UserId, Change("taken@example.com")));
        Assert.Equal("jane.doe@example.com", user.Email);
    }

    [Fact]
    public async Task A_new_email_takes_effect_at_once_but_must_be_verified()
    {
        var user = await VerifiedUserAsync();

        var dto = await _sut.ChangeEmailAsync(user.UserId, Change("New.Address@Example.com"));

        Assert.Equal("new.address@example.com", dto.Email);
        Assert.False(dto.EmailVerified);
        Assert.False(user.EmailVerified);

        var link = _events.OfType<EmailVerificationRequested>().Last();
        Assert.Equal("new.address@example.com", link.Email);
        Assert.True(link.AfterEmailChange);

        var notice = Assert.Single(_events.OfType<EmailChanged>());
        Assert.Equal(("jane.doe@example.com", "new.address@example.com"), (notice.OldEmail, notice.NewEmail));

        Assert.NotNull(await _auth.LoginAsync(new LoginRequest { Email = "new.address@example.com", Password = Password }));
        Assert.Null(await _auth.LoginAsync(new LoginRequest { Email = "jane.doe@example.com", Password = Password }));

        await _sut.VerifyAsync(LastToken);
        Assert.True(user.EmailVerified);
    }

    [Fact]
    public async Task A_link_sent_to_the_old_address_stops_working()
    {
        var user = await RegisterAsync();               // unverified, with a link to jane.doe@
        var oldLink = LastToken;
        _clock.Advance(TimeSpan.FromMinutes(2));

        await _sut.ChangeEmailAsync(user.UserId, Change("new@example.com"));

        await Assert.ThrowsAsync<InvalidVerificationLinkException>(() => _sut.VerifyAsync(oldLink));
    }

    [Fact]
    public async Task A_mistyped_sign_up_address_can_be_fixed_straight_away()
    {
        var user = await RegisterAsync("jane.doe@exmaple.com");   // the sign-up link was sent seconds ago

        var dto = await _sut.ChangeEmailAsync(user.UserId, Change("jane.doe@example.com"));

        Assert.Equal("jane.doe@example.com", dto.Email);
    }

    [Fact]
    public async Task Email_changes_count_toward_the_daily_email_limit()
    {
        var user = await RegisterAsync();                          // 1 link
        for (var i = 1; i <= 9; i++)                                // 9 more
            await _sut.ChangeEmailAsync(user.UserId, Change($"new{i}@example.com"));

        await Assert.ThrowsAsync<TooManyRequestsException>(() => _sut.ChangeEmailAsync(user.UserId, Change("seventh@example.com")));
    }

    // ---------------- Changing the password ----------------

    private static ChangePasswordRequest NewPassword(string current = Password, string next = "N3w!Password") =>
        new() { CurrentPassword = current, NewPassword = next, ConfirmNewPassword = next };

    [Fact]
    public async Task Changing_password_needs_the_current_password()
    {
        var user = await VerifiedUserAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => _auth.ChangePasswordAsync(user.UserId, NewPassword(current: "wrong")));
        Assert.True(BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash));
    }

    [Fact]
    public async Task The_new_password_must_differ_from_the_current_one()
    {
        var user = await VerifiedUserAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => _auth.ChangePasswordAsync(user.UserId, NewPassword(next: Password)));
    }

    [Fact]
    public async Task After_changing_the_password_only_the_new_one_signs_in_and_the_owner_is_told()
    {
        var user = await VerifiedUserAsync();

        await _auth.ChangePasswordAsync(user.UserId, NewPassword());

        Assert.NotNull(await _auth.LoginAsync(new LoginRequest { Email = user.Email, Password = "N3w!Password" }));
        Assert.Null(await _auth.LoginAsync(new LoginRequest { Email = user.Email, Password = Password }));
        Assert.Equal(user.Email, Assert.Single(_events.OfType<PasswordChanged>()).Email);
    }

    [Theory]
    [InlineData("N3w!Password", "N3w!Passwor", false)]   // confirmation doesn't match
    [InlineData("weakpassword", "weakpassword", false)]  // breaks the password rules
    [InlineData("N3w!Password", "N3w!Password", true)]
    public void The_change_password_form_is_validated(string next, string confirm, bool valid)
    {
        var request = new ChangePasswordRequest { CurrentPassword = Password, NewPassword = next, ConfirmNewPassword = confirm };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var ok = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            request, new System.ComponentModel.DataAnnotations.ValidationContext(request), results, validateAllProperties: true);

        Assert.Equal(valid, ok);
    }
}
