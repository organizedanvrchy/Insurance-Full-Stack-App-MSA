using Insurance.BuildingBlocks.Exceptions;

namespace Insurance.Identity.Contracts;

/// <summary>The customer hasn't confirmed their email address yet. Becomes a 403.</summary>
public class EmailNotVerifiedException : ForbiddenException
{
    public EmailNotVerifiedException(string action)
        : base($"Please verify your email address before you {action}. Check your inbox for the link, or send a new one from your profile.") { }
}

public static class EmailVerificationRules
{
    /// <summary>
    /// The rule shared by the Enrollments and Claims services: only customers with a verified email address may
    /// buy a policy or file a claim. Asked of the Identity service on every such request, so it takes effect the
    /// moment the customer clicks the link (no new token needed).
    /// </summary>
    /// <param name="action">Completes "Please verify your email address before you ...", e.g. "file a claim".</param>
    public static async Task EnsureEmailVerifiedAsync(this IUserDirectory users, int userId, string action)
    {
        var user = await users.GetAsync(userId);
        if (user is null || !user.EmailVerified)
            throw new EmailNotVerifiedException(action);
    }
}
