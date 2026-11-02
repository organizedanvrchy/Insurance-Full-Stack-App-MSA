using System.ComponentModel.DataAnnotations;
using Insurance.Identity.Validation;

namespace Insurance.Identity.DTOs;

public class RegisterRequest
{
    // Letters (any language), spaces, hyphens, apostrophes and periods: O'Brien, Anne-Marie, St. John
    private const string NamePattern = @"^[\p{L}\p{M}][\p{L}\p{M}'’.\- ]*$";
    private const string NameMessage = "Use letters, spaces, hyphens and apostrophes only.";

    // 45 + 1 + 45 fits the 100-character FullName column
    [Required, StringLength(45), RegularExpression(NamePattern, ErrorMessage = NameMessage)]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(45), RegularExpression(NamePattern, ErrorMessage = NameMessage)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(PasswordPolicy.MaxLength, ErrorMessage = "Password can be at most 72 characters."), StrongPassword]
    public string Password { get; set; } = string.Empty;

    [UsPhone]
    public string? Phone { get; set; }

    // Address, stored as one line (see ContactFormatter.Address)
    [Required, StringLength(100)]
    public string Street { get; set; } = string.Empty;

    [StringLength(40)]
    public string? Unit { get; set; }      // apartment, suite, unit (optional)

    [Required, StringLength(60), RegularExpression(NamePattern, ErrorMessage = NameMessage)]
    public string City { get; set; } = string.Empty;

    [Required, UsState]
    public string State { get; set; } = string.Empty;

    [Required, RegularExpression(@"^\d{5}(-\d{4})?$", ErrorMessage = "Enter a 5-digit ZIP code.")]
    public string PostalCode { get; set; } = string.Empty;

    // "Email me payment reminders" on the sign-up form. Left out = yes. Changeable later on the profile page.
    public bool? PaymentReminders { get; set; }
}