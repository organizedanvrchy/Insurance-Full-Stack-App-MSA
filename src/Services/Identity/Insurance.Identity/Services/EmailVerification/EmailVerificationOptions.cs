namespace Insurance.Identity.Services;

/// <summary>"EmailVerification" section of the configuration.</summary>
public class EmailVerificationOptions
{
    /// <summary>How long a link in a verification email works.</summary>
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>The shortest time between two verification emails to the same account.</summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Verification emails one account may ask for in 24 hours (registration's own email included).</summary>
    public int MaxEmailsPerDay { get; set; } = 6;
}
