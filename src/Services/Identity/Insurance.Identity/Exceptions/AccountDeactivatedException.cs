namespace Insurance.Identity.Exceptions;

/// <summary>
/// The right email and password, but an administrator deactivated the account. Only someone who knows the password
/// gets this message, so it reveals nothing to someone guessing; everyone else gets "Invalid email or password."
/// The sign-in endpoint answers 403 with it.
/// </summary>
public class AccountDeactivatedException : Exception
{
    public AccountDeactivatedException()
        : base("Your account has been deactivated. Please contact support to have it reactivated.") { }
}
