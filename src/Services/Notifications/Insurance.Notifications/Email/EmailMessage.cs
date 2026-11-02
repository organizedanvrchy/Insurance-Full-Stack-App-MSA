namespace Insurance.Notifications.Email;

/// <summary>A finished email: both an HTML and a plain-text body, as mail clients expect.</summary>
public record EmailMessage(string ToAddress, string ToName, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
