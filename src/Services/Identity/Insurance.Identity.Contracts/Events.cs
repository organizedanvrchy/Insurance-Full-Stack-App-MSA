namespace Insurance.Identity.Contracts;

// Events published by the Identity service. The Notifications service turns each one into an email.
//
// They are published straight to the bus (not through an outbox), because EmailVerificationRequested carries the
// one-time token for the link and the token must never be stored in a database. If publishing fails, the account
// change still stands and the customer can ask for the email again ("Resend").

public enum EmailVerificationPurpose
{
    Registration,   // a new account: confirm the address it was registered with
    EmailChange     // confirm a new address before it replaces the current one
}

/// <summary>Send a verification link to Email. Token goes in the link; only its hash is stored.</summary>
public record EmailVerificationRequested(
    int UserId,
    string FullName,
    string Email,
    string Token,
    EmailVerificationPurpose Purpose,
    DateTime ExpiresAtUtc);

/// <summary>Someone asked to move this account to NewEmail. CurrentEmail gets a heads-up, in case it wasn't them.</summary>
public record EmailChangeRequested(int UserId, string FullName, string CurrentEmail, string NewEmail);

/// <summary>The change was confirmed: NewEmail is now the sign-in address. OldEmail gets a confirmation.</summary>
public record EmailChanged(int UserId, string FullName, string OldEmail, string NewEmail);
