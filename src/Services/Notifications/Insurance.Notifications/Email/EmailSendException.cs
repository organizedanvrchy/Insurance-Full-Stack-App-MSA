namespace Insurance.Notifications.Email;

/// <summary>
/// The mail server didn't take an email. <see cref="TryLater"/> says whether waiting can help: true for its sending
/// limit, a temporary error, a failed sign-in or no connection; false when it refused this email for good
/// (an address it won't deliver to, for example).
/// </summary>
public class EmailSendException : Exception
{
    public EmailSendException(string message, bool tryLater, Exception? inner = null) : base(message, inner) =>
        TryLater = tryLater;

    public bool TryLater { get; }
}
