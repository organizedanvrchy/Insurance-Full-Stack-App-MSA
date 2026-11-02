using Insurance.BuildingBlocks.Security;
using Insurance.Identity.DTOs;
using Insurance.Identity.Exceptions;
using Insurance.Identity.Services;
using Xunit;

namespace Insurance.Identity.Tests;

public class AuthServiceTests
{
    private const string Password = "Str0ng!Pass";

    // xUnit creates a NEW instance of this class for every test, so each test starts with an empty fake.
    private readonly FakeUserRepository _users = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        var clock = new ManualClock();
        var verification = new EmailVerificationService(_users, new FakeEmailTokenRepository(_users, clock), new FakeIdentityEvents(),
            Microsoft.Extensions.Options.Options.Create(new EmailVerificationOptions()), clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EmailVerificationService>.Instance);
        _sut = new AuthService(_users, new FakeTokenService(), verification);
    }

    private static RegisterRequest NewRegistration(string email = "jane.doe@example.com") => new()
    {
        FirstName = "  Jane ",
        LastName = " Doe  ",
        Email = email,
        Password = Password,
        Phone = "203.555.0141",
        Street = " 48  Maple Avenue ",
        Unit = "Apt 3B",
        City = "Stamford",
        State = "ct",
        PostalCode = "06902"
    };

    // ---------------- Register ----------------

    [Fact]
    public async Task Register_stores_a_hash_never_the_plain_password()
    {
        await _sut.RegisterAsync(NewRegistration());

        var saved = _users.Users.Single();
        Assert.NotEqual(Password, saved.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(Password, saved.PasswordHash));
    }

    [Fact]
    public async Task Register_always_creates_a_customer()
    {
        await _sut.RegisterAsync(NewRegistration());

        Assert.Equal(UserRole.Customer, _users.Users.Single().Role);
    }

    [Fact]
    public async Task Register_joins_first_and_last_name_into_the_full_name()
    {
        await _sut.RegisterAsync(NewRegistration());

        Assert.Equal("Jane Doe", _users.Users.Single().FullName);
    }

    [Fact]
    public async Task Register_joins_the_address_parts_into_one_line()
    {
        await _sut.RegisterAsync(NewRegistration());

        Assert.Equal("48 Maple Avenue, Apt 3B, Stamford, CT 06902", _users.Users.Single().Address);
    }

    [Fact]
    public async Task Register_stores_the_phone_in_one_format()
    {
        await _sut.RegisterAsync(NewRegistration());

        Assert.Equal("(203) 555-0141", _users.Users.Single().Phone);
    }

    [Fact]
    public async Task Register_without_a_phone_stores_none()
    {
        var request = NewRegistration();
        request.Phone = null;

        await _sut.RegisterAsync(request);

        Assert.Null(_users.Users.Single().Phone);
    }

    [Fact]
    public async Task Register_lowercases_and_trims_the_email()
    {
        await _sut.RegisterAsync(NewRegistration("  Jane.Doe@Example.COM "));

        Assert.Equal("jane.doe@example.com", _users.Users.Single().Email);
    }

    [Fact]
    public async Task Register_returns_a_token_and_the_new_user()
    {
        var result = await _sut.RegisterAsync(NewRegistration());

        Assert.Equal("token-for-1", result.Token);
        Assert.Equal("jane.doe@example.com", result.User.Email);
        Assert.Equal("Jane Doe", result.User.FullName);
    }

    [Fact]
    public async Task Register_rejects_a_duplicate_email_even_in_a_different_case()
    {
        await _sut.RegisterAsync(NewRegistration("jane.doe@example.com"));

        await Assert.ThrowsAsync<DuplicateEmailException>(
            () => _sut.RegisterAsync(NewRegistration("JANE.DOE@EXAMPLE.COM")));
    }

    // ---------------- Login ----------------

    [Fact]
    public async Task Login_succeeds_with_the_right_password()
    {
        await _sut.RegisterAsync(NewRegistration());

        var result = await _sut.LoginAsync(new LoginRequest { Email = "jane.doe@example.com", Password = Password });

        Assert.NotNull(result);
        Assert.Equal("token-for-1", result!.Token);
    }

    [Fact]
    public async Task Login_ignores_email_capitalization()
    {
        await _sut.RegisterAsync(NewRegistration());

        var result = await _sut.LoginAsync(new LoginRequest { Email = "JANE.Doe@Example.com", Password = Password });

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Login_fails_with_the_wrong_password()
    {
        await _sut.RegisterAsync(NewRegistration());

        var result = await _sut.LoginAsync(new LoginRequest { Email = "jane.doe@example.com", Password = "WrongPassword1!" });

        Assert.Null(result);
    }

    [Fact]
    public async Task Login_fails_for_an_unknown_email()
    {
        var result = await _sut.LoginAsync(new LoginRequest { Email = "nobody@example.com", Password = Password });

        Assert.Null(result);
    }

    [Fact]
    public async Task Login_fails_for_a_deactivated_account_even_with_the_right_password()
    {
        await _sut.RegisterAsync(NewRegistration());
        _users.Users.Single().IsActive = false;

        var result = await _sut.LoginAsync(new LoginRequest { Email = "jane.doe@example.com", Password = Password });

        Assert.Null(result);
    }

    // ---------------- Profile ----------------

    [Fact]
    public async Task Profile_is_returned_for_an_active_user()
    {
        await _sut.RegisterAsync(NewRegistration());

        var profile = await _sut.GetProfileAsync(1);

        Assert.NotNull(profile);
        Assert.Equal("Jane Doe", profile!.FullName);
    }

    [Fact]
    public async Task Profile_is_hidden_for_a_deactivated_user()
    {
        await _sut.RegisterAsync(NewRegistration());
        _users.Users.Single().IsActive = false;

        Assert.Null(await _sut.GetProfileAsync(1));
    }
}