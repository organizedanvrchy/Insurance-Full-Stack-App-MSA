namespace Insurance.Notifications.Email;

/// <summary>"Email" section of the configuration. The password belongs in user secrets or Email__Password, never in git.</summary>
public class EmailOptions
{
    /// <summary>SMTP server. Empty = don't send, just log each email (handy without a mail server).</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;

    /// <summary>None (local test servers such as Mailpit), StartTls (port 587), SslOnConnect (port 465) or Auto.</summary>
    public string Security { get; set; } = "StartTls";

    public string? Username { get; set; }
    public string? Password { get; set; }

    public string FromAddress { get; set; } = "no-reply@rainsurance.example";
    public string FromName { get; set; } = "RAINsurance";

    /// <summary>Where the Angular app lives; links in emails point here.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>
    /// Recipient domains never emailed (e.g. "demo.example" for the demo accounts on a real mail server).
    /// The email is recorded as Skipped instead.
    /// </summary>
    public string[] SkipDomains { get; set; } = Array.Empty<string>();
}
