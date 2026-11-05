using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Insurance.Notifications.Email;

/// <summary>Sends through any SMTP server (Mailpit locally; SendGrid, Azure Communication Services, Gmail... in production).</summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    public SmtpEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        foreach (var file in message.Attachments ?? Array.Empty<EmailAttachment>())
            body.Attachments.Add(file.FileName, file.Content, ContentType.Parse(file.ContentType));
        mime.Body = body.ToMessageBody();

        // A connection per email keeps this simple and robust; the volume here is small.
        try
        {
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_options.Host!, _options.Port, ParseSecurity(_options.Security), ct);
            if (!string.IsNullOrEmpty(_options.Username))
                await smtp.AuthenticateAsync(_options.Username, _options.Password ?? "", ct);
            await smtp.SendAsync(mime, ct);
            await smtp.DisconnectAsync(quit: true, ct);
        }
        catch (Exception ex) when (Classify(ex) is { } failure)
        {
            throw failure;
        }
    }

    /// <summary>
    /// What a mail failure means: worth trying again later, or refused for good. Null for anything else (a bug,
    /// or shutting down), which is left as it is.
    /// </summary>
    public static EmailSendException? Classify(Exception ex) => ex switch
    {
        // 5xx is a permanent "no"... unless it's about sending too much (some servers report limits as 5xx).
        SmtpCommandException c when (int)c.StatusCode >= 500 && !IsAboutLimits(c.Message) =>
            new EmailSendException($"The mail server refused the email: {c.Message}", tryLater: false, ex),
        SmtpCommandException c =>
            new EmailSendException($"The mail server asked to try again later: {c.Message}", tryLater: true, ex),
        // Wrong or expired credentials: emails wait (for a while) until the login is fixed.
        MailKit.Security.AuthenticationException =>
            new EmailSendException($"Signing in to the mail server failed: {ex.Message}", tryLater: true, ex),
        MailKit.Security.SslHandshakeException or SmtpProtocolException or ServiceNotConnectedException
            or IOException or SocketException or TimeoutException =>
            new EmailSendException($"Couldn't reach the mail server: {ex.Message}", tryLater: true, ex),
        _ => null
    };

    private static readonly string[] LimitWords = { "rate", "limit", "quota", "throttl", "exceed", "too many" };

    private static bool IsAboutLimits(string message) =>
        LimitWords.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static SecureSocketOptions ParseSecurity(string value) => value.Trim().ToLowerInvariant() switch
    {
        "none" => SecureSocketOptions.None,
        "starttls" => SecureSocketOptions.StartTls,
        "sslonconnect" or "ssl" => SecureSocketOptions.SslOnConnect,
        "auto" => SecureSocketOptions.Auto,
        _ => throw new InvalidOperationException($"Email:Security must be None, StartTls, SslOnConnect or Auto (was '{value}').")
    };
}

/// <summary>Used when Email:Host is empty: logs what would have been sent.</summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;
    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation("Email (not sent, Email:Host is empty) to {To}: {Subject} [{Attachments} attachment(s)]\n{Body}",
            message.ToAddress, message.Subject, message.Attachments?.Count ?? 0, message.TextBody);
        return Task.CompletedTask;
    }
}
