using Insurance.Notifications.Email;
using Microsoft.Extensions.Options;

namespace Insurance.Notifications.Templates;

/// <summary>Links into the Angular app, built from Email:AppBaseUrl. One place, so the routes are easy to keep in sync.</summary>
public class AppLinks
{
    private readonly string _base;

    public AppLinks(IOptions<EmailOptions> options) : this(options.Value.AppBaseUrl, options.Value.SupportEmail) { }

    public AppLinks(string appBaseUrl, string supportEmail = "support@rainsurance.example")
    {
        _base = appBaseUrl.TrimEnd('/');
        SupportEmail = supportEmail;
    }

    /// <summary>Where customers get help (the From address isn't monitored).</summary>
    public string SupportEmail { get; }

    public string VerifyEmail(string token) => $"{_base}/verify-email?token={Uri.EscapeDataString(token)}";
    public string SignIn => $"{_base}/login";
    public string MyPolicies => $"{_base}/my-policies";
    public string PaymentSchedule(int enrollmentId) => $"{_base}/my-policies/{enrollmentId}/payments";
    public string Receipt(int transactionId) => $"{_base}/receipts/{transactionId}";
    public string MyClaims => $"{_base}/claims";
    public string Profile => $"{_base}/profile";
    public string EmailSettings => $"{_base}/profile#email-settings";
}
