using Insurance.BuildingBlocks.Security;
using Insurance.Identity.DTOs;
using Insurance.Identity.Models;
using Insurance.Identity.Repositories;
using Insurance.Identity.Validation;

namespace Insurance.Identity.Services;

// The service owns BUSINESS rules (hashing, who may register as what, what a login means).
// The repository owns DATA access. The controller owns HTTP.
public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;
    private readonly IEmailVerificationService _verification;

    public AuthService(IUserRepository users, ITokenService tokens, IEmailVerificationService verification)
    {
        _users = users;
        _tokens = tokens;
        _verification = verification;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var user = new User
        {
            FullName = ContactFormatter.FullName(request.FirstName, request.LastName),
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),   // salted + slow on purpose
            Phone = PhoneNumbers.TryNormalize(request.Phone, out var phone) ? phone : null,   // stored in one format
            Address = ContactFormatter.Address(request.Street, request.Unit, request.City, request.State, request.PostalCode),
            Role = UserRole.Customer,      // never trust the client to choose its own role
            CreatedAt = DateTime.UtcNow    // only used for this response; the database stamps the real value
        };

        user.UserId = await _users.CreateAsync(user);

        // New accounts start unverified: they can sign in and browse, but not enroll or file claims yet.
        // Their "email me payment reminders" choice goes to the Notifications service, which owns preferences.
        await _verification.OnRegisteredAsync(user, request.PaymentReminders ?? true);
        return BuildResponse(user);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());

        // Same null result for "no such user", "disabled" and "wrong password" so attackers can't probe which emails exist.
        if (user is null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        return BuildResponse(user);
    }

    public async Task<UserDto?> GetProfileAsync(int userId)
    {
        var user = await _users.GetByIdAsync(userId);
        return user is null || !user.IsActive ? null : MapToDto(user);
    }

    private AuthResponse BuildResponse(User user)
    {
        var (token, expires) = _tokens.CreateToken(user);
        return new AuthResponse { Token = token, ExpiresAtUtc = expires, User = MapToDto(user) };
    }

    internal static UserDto MapToDto(User u) => new()
    {
        UserId = u.UserId,
        FullName = u.FullName,
        Email = u.Email,
        Role = u.Role,
        Phone = u.Phone,
        Address = u.Address,
        // ADO.NET returns DATETIME2 with an unspecified kind, but the column holds UTC.
        CreatedAt = DateTime.SpecifyKind(u.CreatedAt, DateTimeKind.Utc),
        EmailVerified = u.EmailVerified
    };
}
