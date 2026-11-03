using Insurance.BuildingBlocks.Exceptions;
using Insurance.BuildingBlocks.Security;
using Insurance.Identity.Contracts;
using Insurance.Identity.DTOs;
using Insurance.Identity.Exceptions;
using Insurance.Identity.Models;
using Insurance.Identity.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Insurance.Identity.Tests;

// What an admin does in the customer pop-up: edit details (email included), reset the password.
public class AdminCustomerTests
{
    private const string Password = "Str0ng!Pass";

    private readonly ManualClock _clock = new();
    private readonly FakeUserRepository _users = new();
    private readonly FakeIdentityEvents _events = new();
    private readonly EmailVerificationService _verification;
    private readonly AuthService _auth;
    private readonly CustomerService _sut;

    public AdminCustomerTests()
    {
        var tokens = new FakeEmailTokenRepository(_users, _clock);
        _verification = new EmailVerificationService(_users, tokens, _events, Options.Create(new EmailVerificationOptions()),
            _clock, NullLogger<EmailVerificationService>.Instance);
        _auth = new AuthService(_users, new FakeTokenService(), _verification, _events, NullLogger<AuthService>.Instance, _clock);
        _sut = new CustomerService(new FakeCustomerRepository(_users), _verification, _events,
            Options.Create(new CustomerOptions()), _clock, NullLogger<CustomerService>.Instance);
    }

    private async Task<User> CustomerAsync(string email = "jane.doe@example.com")
    {
        await _auth.RegisterAsync(new RegisterRequest
        {
            FirstName = "Jane", LastName = "Doe", Email = email, Password = Password, Phone = "2035550141",
            Street = "48 Maple Avenue", City = "Stamford", State = "CT", PostalCode = "06902"
        });
        var user = _users.Users.Single(u => u.Email == email);
        user.EmailVerifiedAt = DateTime.UtcNow;
        return user;
    }

    private static AdminUpdateCustomerRequest Details(string email = "jane.doe@example.com") => new()
    {
        FirstName = "Janet", LastName = "Smith", Email = email, Phone = "(860) 555-0199",
        Street = "12 Elm Court", Unit = "Apt 2", City = "Hartford", State = "ct", PostalCode = "06103"
    };

    // ---------------- Editing ----------------

    [Fact]
    public async Task An_admin_can_edit_name_phone_and_address()
    {
        var user = await CustomerAsync();

        var info = await _sut.UpdateDetailsAsync(user.UserId, Details());

        Assert.Equal("Janet Smith", info.FullName);
        Assert.Equal("(860) 555-0199", info.Phone);
        Assert.Equal("12 Elm Court, Apt 2, Hartford, CT 06103", info.Address);
        Assert.True(info.EmailVerified);                        // same email: still verified
        Assert.Empty(_events.OfType<EmailChanged>());
    }

    [Fact]
    public async Task An_admin_changing_the_email_makes_it_unverified_and_tells_the_customer()
    {
        var user = await CustomerAsync();

        var info = await _sut.UpdateDetailsAsync(user.UserId, Details("Janet.Smith@Example.com"));

        Assert.Equal("janet.smith@example.com", info.Email);
        Assert.False(info.EmailVerified);

        var notice = Assert.Single(_events.OfType<EmailChanged>());
        Assert.True(notice.ByAdmin);
        Assert.Equal(("jane.doe@example.com", "janet.smith@example.com"), (notice.OldEmail, notice.NewEmail));

        var link = _events.OfType<EmailVerificationRequested>().Last();
        Assert.Equal("janet.smith@example.com", link.Email);
        Assert.True(link.AfterEmailChange && link.ByAdmin);
    }

    [Fact]
    public async Task An_admin_can_change_an_email_right_after_sign_up_without_a_rate_limit()
    {
        var user = await CustomerAsync();
        await _sut.UpdateDetailsAsync(user.UserId, Details("first@example.com"));

        await _sut.UpdateDetailsAsync(user.UserId, Details("second@example.com"));      // no 429

        Assert.Equal("second@example.com", user.Email);
    }

    [Fact]
    public async Task An_email_another_account_uses_is_a_conflict_and_nothing_is_saved()
    {
        var user = await CustomerAsync();
        await CustomerAsync("taken@example.com");

        await Assert.ThrowsAsync<DuplicateEmailException>(() => _sut.UpdateDetailsAsync(user.UserId, Details("taken@example.com")));

        Assert.Equal("Jane Doe", user.FullName);
        Assert.Equal("jane.doe@example.com", user.Email);
    }

    [Fact]
    public async Task Admins_cannot_be_edited_or_reset_here()
    {
        var admin = await CustomerAsync("admin@example.com");
        admin.Role = UserRole.Admin;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.UpdateDetailsAsync(admin.UserId, Details("admin@example.com")));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ResetPasswordAsync(admin.UserId));
    }

    [Theory]
    [InlineData("12345", true)]
    [InlineData("1234", false)]       // ZIP
    public void The_edit_form_follows_the_sign_up_rules(string zip, bool valid)
    {
        var request = Details();
        request.PostalCode = zip;
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        Assert.Equal(valid, System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            request, new System.ComponentModel.DataAnnotations.ValidationContext(request), results, validateAllProperties: true));
    }

    // ---------------- Resetting the password ----------------

    private string LastOneTimePassword => _events.OfType<PasswordResetByAdmin>().Last().OneTimePassword;

    [Fact]
    public async Task A_reset_emails_a_one_time_password_and_the_old_password_stops_working()
    {
        var user = await CustomerAsync();

        var info = await _sut.ResetPasswordAsync(user.UserId);

        Assert.True(info.PasswordResetPending);
        var reset = Assert.Single(_events.OfType<PasswordResetByAdmin>());
        Assert.Equal((user.Email, 30), (reset.Email, reset.ValidForMinutes));
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(30), reset.ExpiresAtUtc);
        Assert.Matches("^.{14}$", reset.OneTimePassword);
        Assert.NotEqual(reset.OneTimePassword, user.PasswordHash);                      // only the hash is stored

        Assert.Null(await _auth.LoginAsync(new LoginRequest { Email = user.Email, Password = Password }));
    }

    [Fact]
    public async Task Signing_in_with_the_one_time_password_requires_a_new_password()
    {
        var user = await CustomerAsync();
        await _sut.ResetPasswordAsync(user.UserId);

        var response = await _auth.LoginAsync(new LoginRequest { Email = user.Email, Password = LastOneTimePassword });

        Assert.NotNull(response);
        Assert.True(response!.User.MustChangePassword);
    }

    [Fact]
    public async Task Choosing_a_new_password_ends_the_reset()
    {
        var user = await CustomerAsync();
        await _sut.ResetPasswordAsync(user.UserId);

        await _auth.ChangePasswordAsync(user.UserId, new ChangePasswordRequest
            { CurrentPassword = LastOneTimePassword, NewPassword = "N3w!Passw0rd", ConfirmNewPassword = "N3w!Passw0rd" });

        Assert.False(user.MustChangePassword);
        var signedIn = await _auth.LoginAsync(new LoginRequest { Email = user.Email, Password = "N3w!Passw0rd" });
        Assert.False(signedIn!.User.MustChangePassword);
    }

    private static ChooseNewPasswordRequest NewPassword(string password = "N3w!Passw0rd") =>
        new() { NewPassword = password, ConfirmNewPassword = password };

    [Fact]
    public async Task After_signing_in_with_the_one_time_password_only_the_new_password_is_needed()
    {
        var user = await CustomerAsync();
        await _sut.ResetPasswordAsync(user.UserId);

        await _auth.ChooseNewPasswordAsync(user.UserId, signedInWithOneTimePassword: true, NewPassword());

        Assert.False(user.MustChangePassword);
        Assert.NotNull(await _auth.LoginAsync(new LoginRequest { Email = user.Email, Password = "N3w!Passw0rd" }));
        Assert.Contains(_events.Published, e => e is PasswordChanged);
    }

    [Fact]
    public async Task A_session_from_before_the_reset_cannot_choose_the_new_password()
    {
        var user = await CustomerAsync();
        await _sut.ResetPasswordAsync(user.UserId);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _auth.ChooseNewPasswordAsync(user.UserId, signedInWithOneTimePassword: false, NewPassword()));
        Assert.True(user.MustChangePassword);
    }

    [Fact]
    public async Task Choosing_a_new_password_without_a_reset_is_refused()
    {
        var user = await CustomerAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _auth.ChooseNewPasswordAsync(user.UserId, signedInWithOneTimePassword: true, NewPassword()));
    }

    [Fact]
    public async Task The_new_password_must_be_chosen_within_the_30_minutes()
    {
        var user = await CustomerAsync();
        await _sut.ResetPasswordAsync(user.UserId);
        _clock.Advance(TimeSpan.FromMinutes(31));

        await Assert.ThrowsAsync<OneTimePasswordExpiredException>(() =>
            _auth.ChooseNewPasswordAsync(user.UserId, signedInWithOneTimePassword: true, NewPassword()));
    }

    [Fact]
    public void Only_a_sign_in_during_a_reset_is_marked_as_using_the_one_time_password()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "unit-test-key-that-is-at-least-32-characters", ["Jwt:Issuer"] = "i", ["Jwt:Audience"] = "a"
        }).Build();
        var tokens = new JwtTokenService(config);
        var user = new User { UserId = 1, Email = "a@b.c", FullName = "A B", Role = UserRole.Customer };

        bool Marked(User u) => new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(tokens.CreateToken(u).Token)
            .Claims.Any(c => c.Type == AuthClaims.SignedInWithOneTimePassword && c.Value == "true");

        Assert.False(Marked(user));
        user.MustChangePassword = true;
        Assert.True(Marked(user));
    }

    [Fact]
    public async Task A_wrong_one_time_password_says_so()
    {
        var user = await CustomerAsync();
        await _sut.ResetPasswordAsync(user.UserId);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _auth.ChangePasswordAsync(user.UserId, new ChangePasswordRequest
            { CurrentPassword = "Wr0ng!Password", NewPassword = "N3w!Passw0rd", ConfirmNewPassword = "N3w!Passw0rd" }));
        Assert.Equal("That one-time password is incorrect.", ex.Message);
    }

    [Fact]
    public async Task The_one_time_password_stops_working_after_30_minutes()
    {
        var user = await CustomerAsync();
        await _sut.ResetPasswordAsync(user.UserId);
        _clock.Advance(TimeSpan.FromMinutes(30));                        // 30 minutes have passed

        await Assert.ThrowsAsync<OneTimePasswordExpiredException>(() =>
            _auth.LoginAsync(new LoginRequest { Email = user.Email, Password = LastOneTimePassword }));
        await Assert.ThrowsAsync<OneTimePasswordExpiredException>(() => _auth.ChangePasswordAsync(user.UserId, new ChangePasswordRequest
            { CurrentPassword = LastOneTimePassword, NewPassword = "N3w!Passw0rd", ConfirmNewPassword = "N3w!Passw0rd" }));
    }

    [Fact]
    public async Task Pressing_reset_again_sends_a_new_one_time_password_and_the_old_one_stops_working()
    {
        var user = await CustomerAsync();
        await _sut.ResetPasswordAsync(user.UserId);
        var first = LastOneTimePassword;

        await _sut.ResetPasswordAsync(user.UserId);

        Assert.NotEqual(first, LastOneTimePassword);
        Assert.Null(await _auth.LoginAsync(new LoginRequest { Email = user.Email, Password = first }));
        Assert.NotNull(await _auth.LoginAsync(new LoginRequest { Email = user.Email, Password = LastOneTimePassword }));
    }

    [Fact]
    public void One_time_passwords_meet_the_password_rules()
    {
        for (var i = 0; i < 50; i++)
            Assert.Empty(Insurance.Identity.Validation.PasswordPolicy.UnmetRules(OneTimePasswords.New()));
    }
}
