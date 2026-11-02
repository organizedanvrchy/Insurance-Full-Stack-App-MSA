namespace Insurance.BuildingBlocks.Exceptions;

/// <summary>
/// The caller asked for the same thing too often (e.g. resending a verification email). Becomes a 429.
/// </summary>
public class TooManyRequestsException : Exception
{
    public TooManyRequestsException(string message) : base(message) { }
}
