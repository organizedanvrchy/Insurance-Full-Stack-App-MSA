namespace Insurance.Notifications.Email;

/// <summary>A file sent with an email (e.g. a copy of a document attached to a claim).</summary>
public record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>A finished email: both an HTML and a plain-text body, as mail clients expect.</summary>
public record EmailMessage(string ToAddress, string ToName, string Subject, string HtmlBody, string TextBody,
    IReadOnlyList<EmailAttachment>? Attachments = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
