using Insurance.Identity.DTOs;
using Insurance.Identity.Models;

namespace Insurance.Identity.Services;

public interface IEmailVerificationService
{
    /// <summary>Emails the link for a new account. Never throws: a failure is logged and the customer can resend.</summary>
    Task SendRegistrationLinkAsync(User user);

    /// <summary>Uses a link. Throws InvalidVerificationLinkException (400) or DuplicateEmailException (409).</summary>
    Task<VerifyEmailResponse> VerifyAsync(string token);

    /// <summary>Emails a fresh link: for the pending new address if there is one, else for the current address.</summary>
    Task ResendAsync(int userId);

    /// <summary>Starts an email change. The new address must be confirmed before it replaces the current one.</summary>
    Task<UserDto> RequestEmailChangeAsync(int userId, ChangeEmailRequest request);

    Task<UserDto> CancelEmailChangeAsync(int userId);
}
