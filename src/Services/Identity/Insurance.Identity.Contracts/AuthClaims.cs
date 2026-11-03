namespace Insurance.Identity.Contracts;

/// <summary>Custom claims in the JWTs that Identity issues, shared with the gateway that reads them.</summary>
public static class AuthClaims
{
    /// <summary>
    /// "true" on a token from signing in with an admin-reset one-time password. Only such a session may choose the
    /// new password without typing a current one; sessions that were open before the reset are signed out instead.
    /// </summary>
    public const string SignedInWithOneTimePassword = "otp_sign_in";
}
