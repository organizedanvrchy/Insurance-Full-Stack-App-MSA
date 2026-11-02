namespace Insurance.BuildingBlocks.Exceptions;

/// <summary>
/// The caller is signed in but may not do this yet, for example before verifying their email address.
/// Becomes a 403.
/// </summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
