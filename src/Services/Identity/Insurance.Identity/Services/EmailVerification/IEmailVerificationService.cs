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
}
