using Insurance.BuildingBlocks.Exceptions;

namespace Insurance.Identity.Exceptions;

/// <summary>
/// The customer used the one-time password from an admin reset after it expired. Only an administrator can send a
/// new one. A 400 from change-password; the sign-in endpoint answers 401 with this message.
/// </summary>
public class OneTimePasswordExpiredException : BusinessRuleException
{
    public OneTimePasswordExpiredException()
        : base("Your one-time password has expired. Please contact us to have a new one sent.") { }
}
