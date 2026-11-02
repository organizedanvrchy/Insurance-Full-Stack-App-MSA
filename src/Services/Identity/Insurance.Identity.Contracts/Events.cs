namespace Insurance.Identity.Contracts;

// Events published by the Identity service. The Notifications service acts on them.
//
// They are published straight to the bus (not through an outbox), because EmailVerificationRequested carries the
// one-time token for the link and the token must never be stored in a database. If publishing fails, the account
// still exists and the customer can ask for the email again ("Resend").

/// <summary>A customer signed up. PaymentReminders is their answer to "email me payment reminders" on the form.</summary>
public record CustomerRegistered(int UserId, bool PaymentReminders);

/// <summary>Send a verification link to Email. Token goes in the link; only its hash is stored.</summary>
public record EmailVerificationRequested(
    int UserId,
    string FullName,
    string Email,
    string Token,
    DateTime ExpiresAtUtc);
