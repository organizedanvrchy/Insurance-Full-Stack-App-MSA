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
/// AfterEmailChange: the account was just changed to this address (rather than signing up with it).
/// ValidForMinutes: how long the link works, for the email's wording. ByAdmin: an administrator made the change.
/// </summary>
public record EmailVerificationRequested(
    int UserId,
    string FullName,
    string Email,
    string Token,
    DateTime ExpiresAtUtc,
    bool AfterEmailChange = false,
    int ValidForMinutes = 0,
    bool ByAdmin = false);

/// <summary>The sign-in email was changed (ByAdmin: by an administrator). OldEmail is told.</summary>
public record EmailChanged(int UserId, string FullName, string OldEmail, string NewEmail, DateTime ChangedAtUtc,
    bool ByAdmin = false);

/// <summary>
/// An administrator reset the password. OneTimePassword goes in the email (only its hash is stored); the customer
/// enters it at their next sign-in and must then choose a new password. It stops working at ExpiresAtUtc.
/// </summary>
public record PasswordResetByAdmin(int UserId, string FullName, string Email, string OneTimePassword, DateTime ExpiresAtUtc,
    int ValidForMinutes);

/// <summary>
/// An administrator deactivated an active account: the customer can no longer sign in. The email tells them, and
/// that support can reactivate it. Not published when the account was already inactive.
/// </summary>
public record AccountDeactivated(int UserId, string FullName, string Email, DateTime DeactivatedAtUtc);

/// <summary>The password was changed. Email is told, in case it wasn't the account's owner.</summary>
public record PasswordChanged(int UserId, string FullName, string Email, DateTime ChangedAtUtc);
