using System.ComponentModel.DataAnnotations;

namespace Insurance.Identity.DTOs;

/// <summary>POST /api/auth/verify-email: the token from the link in the email.</summary>
public class VerifyEmailRequest
{
    [Required, StringLength(200)]
    public string Token { get; set; } = string.Empty;
}

/// <summary>The address that is now verified.</summary>
public class VerifyEmailResponse
{
    public string Email { get; set; } = string.Empty;
}
