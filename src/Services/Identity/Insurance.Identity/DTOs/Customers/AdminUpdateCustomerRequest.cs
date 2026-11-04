using System.ComponentModel.DataAnnotations;
using Insurance.Identity.Validation;

namespace Insurance.Identity.DTOs;

/// <summary>PUT /internal/customers/{id}: what an admin can change. The same rules as the sign-up form.</summary>
public class AdminUpdateCustomerRequest
{
    private const string NamePattern = @"^[\p{L}\p{M}][\p{L}\p{M}'’.\- ]*$";
    private const string NameMessage = "Use letters, spaces, hyphens and apostrophes only.";

    [Required, StringLength(45), RegularExpression(NamePattern, ErrorMessage = NameMessage)]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(45), RegularExpression(NamePattern, ErrorMessage = NameMessage)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress(ErrorMessage = EmailRules.Message), RegularExpression(EmailRules.Pattern, ErrorMessage = EmailRules.Message), StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [UsPhone]
    public string? Phone { get; set; }

    [Required, StringLength(100)]
    public string Street { get; set; } = string.Empty;

    [StringLength(40)]
    public string? Unit { get; set; }

    [Required, StringLength(60), RegularExpression(NamePattern, ErrorMessage = NameMessage)]
    public string City { get; set; } = string.Empty;

    [Required, UsState]
    public string State { get; set; } = string.Empty;

    [Required, RegularExpression(@"^\d{5}(-\d{4})?$", ErrorMessage = "Enter a 5-digit ZIP code.")]
    public string PostalCode { get; set; } = string.Empty;
}
