namespace Insurance.Identity.Contracts;

// Events published by the Identity service. The Notifications service acts on them.
//
// They are published straight to the bus (not through an outbox), because EmailVerificationRequested carries the
// one-time token for the link and the token must never be stored in a database. If publishing fails, the account
// change still stands, and the customer can ask for the verification email again ("Resend").

/// <summary>A customer signed up. PaymentReminders is their answer to "email me payment reminders" on the form.</summary>
public record CustomerRegistered(int UserId, bool PaymentReminders);

/// <summary>
/// Send a verification link to Email. Token goes in the link; only its hash is stored.
/// AfterEmailChange: the customer just changed to this address (rather than signing up with it).
/// </summary>
public record EmailVerificationRequested(
    int UserId,
    string FullName,
    string Email,
    string Token,
    DateTime ExpiresAtUtc,
    bool AfterEmailChange = false);

/// <summary>The sign-in email was changed. OldEmail is told, in case it wasn't the account's owner.</summary>
public record EmailChanged(int UserId, string FullName, string OldEmail, string NewEmail, DateTime ChangedAtUtc);

/// <summary>The password was changed. Email is told, in case it wasn't the account's owner.</summary>
public record PasswordChanged(int UserId, string FullName, string Email, DateTime ChangedAtUtc);
