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

    public string FromAddress { get; set; } = "no-reply@kvbinsurance.example";
    public string FromName { get; set; } = Templates.EmailBuilder.Brand;

    /// <summary>
    /// Where customers can get help. Shown in every email's "automated message, don't reply" notice, because the
    /// From address is not monitored.
    /// </summary>
    public string SupportEmail { get; set; } = "support@kvbinsurance.example";

    /// <summary>Where the Angular app lives; links in emails point here.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>
    /// The most an email's attachments may add up to. Most mail providers refuse messages over about 20-25 MB; files
    /// beyond this are still listed in the email, with a note to download them from the app.
    /// </summary>
    public int MaxAttachmentBytes { get; set; } = 15 * 1024 * 1024;

    /// <summary>
    /// Recipient domains never emailed (e.g. "demo.example" for the demo accounts on a real mail server).
    /// The email is recorded as Skipped instead.
    /// </summary>
    public string[] SkipDomains { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Also skip the domains reserved for documentation and testing (RFC 2606 / 6761: example.com, example.net,
    /// example.org and anything under .example, .test, .invalid or .localhost). A real mail server refuses them
    /// ("Recipient address reserved by RFC 2606"), so test and demo accounts would only produce failures. Off by
    /// default, because the local catch-all (Mailpit) and the end-to-end tests deliberately use such addresses.
    /// </summary>
    public bool SkipReservedDomains { get; set; }
}
