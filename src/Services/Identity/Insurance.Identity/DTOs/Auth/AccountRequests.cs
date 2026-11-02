using System.ComponentModel.DataAnnotations;
using Insurance.Identity.Validation;

namespace Insurance.Identity.DTOs;

/// <summary>POST /api/auth/change-email. The current password proves it's the account's owner.</summary>
public class ChangeEmailRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string NewEmail { get; set; } = string.Empty;

    [Required]
    public string CurrentPassword { get; set; } = string.Empty;
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
