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
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        // A connection per email keeps this simple and robust; the volume here is small.
        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_options.Host!, _options.Port, ParseSecurity(_options.Security), ct);
        if (!string.IsNullOrEmpty(_options.Username))
            await smtp.AuthenticateAsync(_options.Username, _options.Password ?? "", ct);
        await smtp.SendAsync(mime, ct);
        await smtp.DisconnectAsync(quit: true, ct);
    }

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
        _logger.LogInformation("Email (not sent, Email:Host is empty) to {To}: {Subject}\n{Body}",
            message.ToAddress, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}
