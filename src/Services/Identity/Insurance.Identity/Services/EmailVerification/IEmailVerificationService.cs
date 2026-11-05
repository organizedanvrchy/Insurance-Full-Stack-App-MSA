using Insurance.Identity.DTOs;
using Insurance.Identity.Models;

namespace Insurance.Identity.Services;

public interface IEmailVerificationService
{
    /// <summary>
    /// A new account: records the customer's email choice (CustomerRegistered) and emails the verification link.
    /// Never throws: a failure is logged and the customer can press "Resend".
    /// </summary>
    Task OnRegisteredAsync(User user, bool paymentReminders);

    /// <summary>Uses a link. Throws InvalidVerificationLinkException (400).</summary>
    Task<VerifyEmailResponse> VerifyAsync(string token);

    /// <summary>Emails a fresh link. 409 if already verified, 429 if asked too often.</summary>
    Task ResendAsync(int userId);

    /// <summary>
    /// Changes the sign-in email. The new address is unverified until its link is used, and the old address is told.
    /// 400 invalid or same address, 409 address taken, 429 too many emails.
    /// </summary>
    Task<UserDto> ChangeEmailAsync(int userId, ChangeEmailRequest request);

    /// <summary>The same, made by an administrator: no rate limit, and the emails say an administrator did it.</summary>
    Task ChangeEmailByAdminAsync(int userId, string newEmail);
}
