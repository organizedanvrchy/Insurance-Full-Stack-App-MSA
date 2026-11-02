namespace Insurance.Identity.Validation;

/// <summary>
/// A deliverable-looking address: something@domain.tld. [EmailAddress] alone accepts "a@b", which no mail server
/// would deliver to. Used with [RegularExpression] on sign-up and change-email; the Angular forms use the same pattern.
/// </summary>
public static class EmailRules
{
    public const string Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$";
    public const string Message = "Enter a valid email address, like name@example.com.";
}
