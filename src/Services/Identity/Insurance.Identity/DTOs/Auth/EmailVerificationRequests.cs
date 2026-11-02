using System.ComponentModel.DataAnnotations;

namespace Insurance.Identity.DTOs;

/// <summary>POST /api/auth/verify-email: the token from the link in the email.</summary>
public class VerifyEmailRequest
{
    [Required, StringLength(200)]
    public string Token { get; set; } = string.Empty;
}

/// <summary>The result of using a link. EmailChanged is true when it confirmed a new address.</summary>
public class VerifyEmailResponse
{
    public string Email { get; set; } = string.Empty;
    public bool EmailChanged { get; set; }
}

/// <summary>POST /api/auth/change-email. The current password is required, so a borrowed session can't take over the account.</summary>
public class ChangeEmailRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string NewEmail { get; set; } = string.Empty;

    [Required]
    public string CurrentPassword { get; set; } = string.Empty;
}
