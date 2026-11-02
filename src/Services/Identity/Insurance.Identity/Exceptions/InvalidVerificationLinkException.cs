using Insurance.BuildingBlocks.Exceptions;

namespace Insurance.Identity.Exceptions;

/// <summary>The link's token is unknown, expired, already used or replaced by a newer one (SQL error 50022). Becomes a 400.</summary>
public class InvalidVerificationLinkException : BusinessRuleException
{
    public InvalidVerificationLinkException()
        : base("This verification link is invalid, has expired or has already been used. Sign in to request a new one.") { }
}
