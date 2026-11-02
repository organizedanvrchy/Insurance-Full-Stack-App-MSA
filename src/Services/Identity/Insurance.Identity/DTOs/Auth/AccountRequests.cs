using System.ComponentModel.DataAnnotations;
using Insurance.Identity.Validation;

namespace Insurance.Identity.DTOs;

/// <summary>
/// POST /api/auth/change-email. Only the new address: being signed in is enough. The old address is told, and the new
/// one must be verified before the customer can enroll or file claims.
/// </summary>
public class ChangeEmailRequest
{
    [Required(ErrorMessage = "Enter your new email address."), EmailAddress(ErrorMessage = EmailRules.Message),
     RegularExpression(EmailRules.Pattern, ErrorMessage = EmailRules.Message), StringLength(256)]
    public string NewEmail { get; set; } = string.Empty;
}

/// <summary>POST /api/auth/change-password. The new password follows the same rules as at sign-up.</summary>
public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(PasswordPolicy.MaxLength, ErrorMessage = "Password can be at most 72 characters."), StrongPassword]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(NewPassword), ErrorMessage = "The new passwords don't match.")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}
