using System.Net;
using System.Text;

namespace Insurance.Notifications.Templates;

/// <summary>A rendered email before it has a recipient. Attachments are files sent with it.</summary>
public record RenderedEmail(string Subject, string Html, string Text)
{
    public IReadOnlyList<Insurance.Notifications.Email.EmailAttachment> Attachments { get; init; } =
        Array.Empty<Insurance.Notifications.Email.EmailAttachment>();
}

/// <summary>
/// Builds the HTML and the plain-text version of an email side by side, from the same blocks, so the two never
/// drift apart. Every value is HTML-encoded here: templates can pass customer-entered text (names, claim notes)
/// without thinking about it. The HTML uses tables and inline styles, which is what mail clients render reliably.
/// </summary>
public sealed class EmailBuilder
{
    /// <summary>The company name in every email (subjects, text and footer). Change it here to rebrand them all.</summary>
    public const string Brand = "KVBInsurance";
    private const string Primary = "#12544f";   // the website's pine green
    private const string Ink = "#092328";
    private const string Muted = "#5e6b45";
    private const string Border = "#eae2d6";

    private readonly string _subject;
    private readonly string _preheader;
    private readonly StringBuilder _html = new();
    private readonly StringBuilder _text = new();
    private string? _footerNote;

    private readonly string _supportEmail;

    /// <param name="preheader">The preview line most inboxes show next to the subject.</param>
    /// <param name="supportEmail">Where to write instead of replying (every email is automated).</param>
    public EmailBuilder(string subject, string preheader, string supportEmail = "support@kvbinsurance.example")
    {
        _subject = subject;
        _preheader = preheader;
        _supportEmail = supportEmail;
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);

    public EmailBuilder Heading(string text)
    {
        _html.Append($"<h1 style=\"margin:0 0 16px;font-size:22px;line-height:1.3;color:{Ink};\">{E(text)}</h1>");
        _text.AppendLine(text).AppendLine(new string('=', Math.Min(text.Length, 60))).AppendLine();
        return this;
    }

    public EmailBuilder Paragraph(string text)
    {
        _html.Append($"<p style=\"margin:0 0 16px;font-size:15px;line-height:1.6;color:{Ink};\">{E(text)}</p>");
        _text.AppendLine(text).AppendLine();
        return this;
    }

    /// <summary>A highlighted box, for the one thing the customer must not miss (e.g. a rejection reason).</summary>
    public EmailBuilder Callout(string title, string text, bool warning = false)
    {
        var (bg, edge) = warning ? ("#fff4e5", "#b45309") : ("#eef1e8", Primary);
        _html.Append($"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 16px;border-collapse:collapse;">
              <tr><td style="background:{bg};border-left:4px solid {edge};padding:12px 16px;border-radius:4px;">
                <p style="margin:0 0 4px;font-size:13px;font-weight:bold;color:{Ink};">{E(title)}</p>
                <p style="margin:0;font-size:14px;line-height:1.5;color:{Ink};white-space:pre-line;">{E(text)}</p>
              </td></tr>
            </table>
            """);
        var separator = title.EndsWith('?') || title.EndsWith('!') ? " " : ": ";
        _text.AppendLine($"{title}{separator}{text}").AppendLine();
        return this;
    }

    /// <summary>A code the customer has to type, e.g. a one-time password: large, monospaced, easy to select.</summary>
    public EmailBuilder Code(string label, string code)
    {
        _html.Append($"""
            <p style="margin:0 0 6px;font-size:13px;color:{Muted};">{E(label)}</p>
            <p style="margin:0 0 20px;padding:14px 16px;background:#eef1e8;border-radius:6px;text-align:center;font-family:Consolas,Menlo,monospace;font-size:22px;letter-spacing:2px;color:{Ink};">{E(code)}</p>
            """);
        _text.AppendLine($"{label}: {code}").AppendLine();
        return this;
    }

    /// <summary>A two-column table of facts (receipt lines, claim details).</summary>
    public EmailBuilder Details(params (string Label, string Value)[] rows)
    {
        _html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:0 0 20px;border-collapse:collapse;border:1px solid {Border};border-radius:6px;\">");
        foreach (var (label, value) in rows)
        {
            _html.Append($"""
                <tr>
                  <td style="padding:10px 14px;border-bottom:1px solid {Border};font-size:14px;color:{Muted};width:45%;">{E(label)}</td>
                  <td style="padding:10px 14px;border-bottom:1px solid {Border};font-size:14px;color:{Ink};font-weight:bold;text-align:right;">{E(value)}</td>
                </tr>
                """);
            _text.AppendLine($"  {label + ":",-24} {value}");
        }
        _html.Append("</table>");
        _text.AppendLine();
        return this;
    }

    public EmailBuilder Button(string label, string url)
    {
        _html.Append($"""
            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:4px 0 24px;">
              <tr><td style="background:{Primary};border-radius:6px;">
                <a href="{E(url)}" style="display:inline-block;padding:12px 22px;font-size:15px;font-weight:bold;color:#f7f2eb;text-decoration:none;">{E(label)}</a>
              </td></tr>
            </table>
            <p style="margin:0 0 16px;font-size:12px;line-height:1.5;color:{Muted};">If the button doesn't work, copy this link into your browser:<br><a href="{E(url)}" style="color:{Primary};word-break:break-all;">{E(url)}</a></p>
            """);
        _text.AppendLine($"{label}: {url}").AppendLine();
        return this;
    }

    public EmailBuilder Small(string text)
    {
        _html.Append($"<p style=\"margin:0 0 12px;font-size:13px;line-height:1.5;color:{Muted};\">{E(text)}</p>");
        _text.AppendLine(text).AppendLine();
        return this;
    }

    /// <summary>An extra line in the footer, e.g. how to turn reminders off.</summary>
    public EmailBuilder FooterNote(string text, string? linkLabel = null, string? linkUrl = null)
    {
        var link = linkLabel is null || linkUrl is null ? "" : $" <a href=\"{E(linkUrl)}\" style=\"color:{Muted};\">{E(linkLabel)}</a>";
        _footerNote = E(text) + link;
        _text.AppendLine(linkUrl is null ? text : $"{text} {linkLabel}: {linkUrl}").AppendLine();
        return this;
    }

    public RenderedEmail Build()
    {
        var footerNote = _footerNote is null ? "" : $"<p style=\"margin:0 0 8px;\">{_footerNote}</p>";
        var support = E(_supportEmail);
        var automated = $"<p style=\"margin:0 0 8px;\"><strong>This is an automated message. Please don't reply to this email</strong>: replies aren't read. If you need help, contact <a href=\"mailto:{support}\" style=\"color:{Muted};\">{support}</a>.</p>";
        var html = $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(_subject)}</title></head>
            <body style="margin:0;padding:0;background:#f7f2eb;font-family:Segoe UI,Helvetica,Arial,sans-serif;">
              <span style="display:none;max-height:0;overflow:hidden;opacity:0;">{E(_preheader)}</span>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f7f2eb;padding:24px 12px;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border:1px solid {Border};border-radius:10px;overflow:hidden;">
                    <tr><td style="background:{Primary};padding:18px 28px;">
                      <span style="font-size:20px;font-weight:bold;color:#f7f2eb;letter-spacing:0.3px;">&#127810; {Brand}</span>
                    </td></tr>
                    <tr><td style="padding:28px;">{_html}</td></tr>
                    <tr><td style="padding:18px 28px;border-top:1px solid {Border};font-size:12px;line-height:1.5;color:{Muted};">
                      {automated}
                      {footerNote}
                      <p style="margin:0;">You're receiving this email because you have a {Brand} account. {Brand} is a fictional insurer created for a portfolio project.</p>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var text = _text.ToString().TrimEnd()
            + $"\n\n--\nThis is an automated message. Please don't reply to this email: replies aren't read. If you need help, contact {_supportEmail}.\n"
            + $"{Brand}. A fictional insurer created for a portfolio project.\n";
        return new RenderedEmail(_subject, html, text);
    }
}
