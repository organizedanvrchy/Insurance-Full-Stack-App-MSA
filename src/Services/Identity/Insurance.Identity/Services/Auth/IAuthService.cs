using Insurance.Identity.DTOs;

namespace Insurance.Identity.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);   // throws DuplicateEmailException
    Task<AuthResponse?> LoginAsync(LoginRequest request);        // null => bad credentials
    Task<UserDto?> GetProfileAsync(int userId);

    // 400 if the current password is wrong or the new one is the same. The account's email is told.
    Task ChangePasswordAsync(int userId, ChangePasswordRequest request);
}
