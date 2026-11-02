using Insurance.Billing.Contracts;
using Insurance.BuildingBlocks.Time;
using Insurance.Claims.Contracts;
using Insurance.Identity.Contracts;
using Insurance.Notifications.Templates;
using Xunit;

namespace Insurance.Notifications.Tests;

public class TemplateTests
{
    private readonly EmailTemplates _templates = new(new AppLinks("http://app.test/"));

    private static PaymentReminderDue Reminder(PaymentReminderStage stage, int daysPastDue = 0) =>
        new(100, 10, 1, 4, new DateOnly(2026, 11, 20), 1234.5m, stage, daysPastDue);

    [Fact]
    public void Customer_text_is_html_encoded()
    {
        var e = new ClaimStatusChanged(5, "CLM-2026-000005", 1, 10, 900m, ClaimStatus.UnderReview, ClaimStatus.Rejected,
            "<script>alert('x')</script> Not covered", DateTime.UtcNow);

        var email = _templates.ClaimStatusChanged("<b>Jane</b> Doe", e, "Home & Garden");

        Assert.DoesNotContain("<script>", email.Html);
        Assert.Contains("&lt;script&gt;", email.Html);
        Assert.Contains("Home &amp; Garden", email.Html);
        Assert.Contains("<script>alert('x')</script> Not covered", email.Text);   // plain text needs no encoding
    }

    [Fact]
    public void The_verification_link_carries_the_token()
    {
        var e = new EmailVerificationRequested(1, "Jane Doe", "jane@example.com", "abc_DEF-123",
            new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc));

        var email = _templates.VerifyEmail(e);

        Assert.Equal("Verify your email address", email.Subject);
        Assert.Contains("http://app.test/verify-email?token=abc_DEF-123", email.Html);
        Assert.Contains("http://app.test/verify-email?token=abc_DEF-123", email.Text);
        Assert.Contains("expires on November 3, 2026 at 4:00 AM EST", email.Text);
        Assert.Contains("You won't be able to enroll in any policies or file claims until you verify your email.", email.Text);
        Assert.Contains("Hi Jane,", email.Text);
    }

    [Theory]
    [InlineData(PaymentReminderStage.DueSoon, 0, "Your Gold Health Plan premium is due on November 20, 2026")]
    [InlineData(PaymentReminderStage.DueToday, 0, "Your Gold Health Plan premium is due today")]
    [InlineData(PaymentReminderStage.Overdue, 6, "Your Gold Health Plan premium is overdue")]
    [InlineData(PaymentReminderStage.OverdueSecondNotice, 11, "Reminder: your Gold Health Plan premium is 11 days overdue")]
    [InlineData(PaymentReminderStage.OverdueFinalNotice, 16, "Final notice: your Gold Health Plan premium is 16 days overdue")]
    public void Each_reminder_stage_has_its_own_subject(PaymentReminderStage stage, int daysPastDue, string subject)
    {
        var email = _templates.PaymentReminder("Jane Doe", Reminder(stage, daysPastDue), "Gold Health Plan");

        Assert.Equal(subject, email.Subject);
        Assert.Contains("$1,234.50", email.Text);
        Assert.Contains("http://app.test/my-policies/10/payments", email.Text);   // "Pay now"
    }

    [Fact]
    public void Reminders_say_how_to_turn_them_off()
    {
        var email = _templates.PaymentReminder("Jane Doe", Reminder(PaymentReminderStage.DueSoon), "Gold Health Plan");

        Assert.Contains("Don't want payment reminders?", email.Text);
        Assert.Contains("http://app.test/profile#email-settings", email.Html);
    }

    [Fact]
    public void Overdue_reminders_warn_that_claims_are_blocked()
    {
        var overdue = _templates.PaymentReminder("Jane Doe", Reminder(PaymentReminderStage.Overdue, 6), "Gold Health Plan");
        var soon = _templates.PaymentReminder("Jane Doe", Reminder(PaymentReminderStage.DueSoon), "Gold Health Plan");

        Assert.Contains("can't file new claims", overdue.Text);
        Assert.DoesNotContain("can't file new claims", soon.Text);
    }

    [Fact]
    public void A_receipt_lists_the_payment_details()
    {
        var e = new PaymentReceived(42, 7, "TXN-000007", 10, 1, 4, 89.99m, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2),
            DateTime.UtcNow, "Visa ending in 4242", InstallmentNumber: 3, InstallmentCount: 12, new DateOnly(2026, 12, 1), 89.99m);

        var email = _templates.PaymentReceipt("Jane Doe", e, "Gold Health Plan");

        Assert.Equal("Payment receipt TXN-000007", email.Subject);
        Assert.Contains("TXN-000007", email.Text);
        Assert.Contains("Visa ending in 4242", email.Text);
        Assert.Contains("http://app.test/receipts/7", email.Text);     // the in-app receipt
        Assert.Contains("$89.99", email.Text);
        Assert.Contains("3 of 12", email.Text);
        Assert.Matches(@"Paid on:\s+November \d+, 2026 at \d{1,2}:\d\d (AM|PM) E[SD]T", email.Text);   // date AND time
        Assert.Contains("$89.99 on December 1, 2026", email.Text);         // next payment
    }

    [Fact]
    public void The_last_receipt_says_the_policy_is_paid_up()
    {
        var e = new PaymentReceived(42, 7, "TXN-000007", 10, 1, 4, 89.99m, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2),
            DateTime.UtcNow, "PayPal", 12, 12, null, null);

        var email = _templates.PaymentReceipt("Jane Doe", e, "Gold Health Plan");

        Assert.Contains("fully paid up", email.Text);
        Assert.Contains("last installment", email.Text);
    }

    [Theory]
    [InlineData(false, "check the details or use a different payment method")]
    [InlineData(true, "try again in a few minutes")]
    public void A_declined_payment_explains_what_happened(bool canRetry, string advice)
    {
        var e = new PaymentDeclined(7, "TXN-000007", 42, 10, 1, 4, 89.99m, new DateOnly(2026, 11, 2), "Visa ending in 0002",
            "Your card was declined.", canRetry, DateTime.UtcNow);

        var email = _templates.PaymentDeclined("Jane Doe", e, "Gold Health Plan");

        Assert.Equal("Your Gold Health Plan payment didn't go through", email.Subject);
        Assert.Contains("Reason: Your card was declined.", email.Text);
        Assert.Contains("You have not been charged", email.Text);
        Assert.Contains("Visa ending in 0002", email.Text);
        Assert.Contains(advice, email.Text);
        Assert.Contains("http://app.test/my-policies/10/payments", email.Text);   // "Try again"
    }

    [Theory]
    [InlineData(ClaimStatus.UnderReview, "Your claim CLM-1 is under review")]
    [InlineData(ClaimStatus.Approved, "Good news: claim CLM-1 was approved")]
    [InlineData(ClaimStatus.Rejected, "An update on your claim CLM-1")]
    [InlineData(ClaimStatus.Paid, "Claim CLM-1 has been paid")]
    public void Each_claim_status_has_its_own_subject(ClaimStatus status, string subject)
    {
        var e = new ClaimStatusChanged(1, "CLM-1", 1, 10, 500m, ClaimStatus.Submitted, status, null, DateTime.UtcNow);

        Assert.Equal(subject, _templates.ClaimStatusChanged("Jane Doe", e, "Gold Health Plan").Subject);
    }

    [Fact]
    public void A_rejection_shows_the_reason()
    {
        var e = new ClaimStatusChanged(1, "CLM-1", 1, 10, 500m, ClaimStatus.UnderReview, ClaimStatus.Rejected,
            "Flood damage is excluded from this plan.", DateTime.UtcNow);

        var email = _templates.ClaimStatusChanged("Jane Doe", e, "Gold Health Plan");

        Assert.Contains("Reason: Flood damage is excluded from this plan.", email.Text);
    }

    [Theory]
    [InlineData("Jane Doe", "Jane")]
    [InlineData("  Anne-Marie   O'Brien ", "Anne-Marie")]
    [InlineData("Cher", "Cher")]
    public void Greets_customers_by_first_name(string fullName, string expected)
    {
        Assert.Equal(expected, EmailTemplates.FirstName(fullName));
    }

    // ---------------- Every email ----------------

    public static TheoryData<string> AllEmails() => new(new[]
    {
        "verify", "verify-after-change", "email-changed", "password-changed", "policy-active", "policy-cancelled",
        "claim-received", "claim-status", "receipt", "declined", "reminder"
    });

    private RenderedEmail Render(string name)
    {
        var due = new DateOnly(2026, 11, 20);
        var policy = new PolicySummary("Gold Health Plan", 50_000m, due, due.AddYears(1));
        return name switch
        {
            "verify" => _templates.VerifyEmail(new EmailVerificationRequested(1, "Jane Doe", "j@example.com", "t", DateTime.UtcNow)),
            "verify-after-change" => _templates.VerifyEmail(new EmailVerificationRequested(1, "Jane Doe", "j@example.com", "t", DateTime.UtcNow, true)),
            "email-changed" => _templates.EmailChanged(new EmailChanged(1, "Jane Doe", "old@example.com", "new@example.com", DateTime.UtcNow)),
            "password-changed" => _templates.PasswordChanged(new PasswordChanged(1, "Jane Doe", "j@example.com", DateTime.UtcNow)),
            "policy-active" => _templates.PolicyActivated("Jane Doe", policy),
            "policy-cancelled" => _templates.PolicyCancelled("Jane Doe", policy),
            "claim-received" => _templates.ClaimSubmitted("Jane Doe", new ClaimSubmitted(1, "CLM-1", 1, 10, due, 500m, DateTime.UtcNow), "Gold"),
            "claim-status" => _templates.ClaimStatusChanged("Jane Doe", new ClaimStatusChanged(1, "CLM-1", 1, 10, 500m, ClaimStatus.Submitted, ClaimStatus.UnderReview, null, DateTime.UtcNow), "Gold"),
            "receipt" => _templates.PaymentReceipt("Jane Doe", new PaymentReceived(42, 7, "TXN-000007", 10, 1, 4, 9m, due, due, DateTime.UtcNow, "PayPal", 1, 12, null, null), "Gold"),
            "declined" => _templates.PaymentDeclined("Jane Doe", new PaymentDeclined(7, "TXN-000007", 42, 10, 1, 4, 9m, due, "PayPal", "No.", false, DateTime.UtcNow), "Gold"),
            _ => _templates.PaymentReminder("Jane Doe", Reminder(PaymentReminderStage.DueToday), "Gold")
        };
    }

    [Theory]
    [MemberData(nameof(AllEmails))]
    public void Every_email_says_it_is_automated_and_where_to_get_help(string name)
    {
        var email = Render(name);

        Assert.Contains("This is an automated message. Please don't reply to this email", email.Text);
        Assert.Contains("support@rainsurance.example", email.Text);
        Assert.Contains("This is an automated message.", email.Html);
        Assert.Contains("mailto:support@rainsurance.example", email.Html);
    }

    [Fact]
    public void A_received_claim_sets_expectations_for_the_review()
    {
        Assert.Contains("A claims specialist will review your claim, and we'll email you when it has been reviewed. "
            + "Please allow at least 2 business days for a response.", Render("claim-received").Text);
    }

    [Fact]
    public void The_policy_email_no_longer_promises_reminders()
    {
        Assert.DoesNotContain("We'll remind you", Render("policy-active").Text);
    }

    [Fact]
    public void The_old_address_is_asked_whether_the_change_was_theirs()
    {
        var email = Render("email-changed");

        Assert.Equal("Your RAINsurance email address was changed", email.Subject);
        Assert.Contains("from old@example.com to new@example.com", email.Text);
        Assert.Contains("If you made this change, you can ignore this email.", email.Text);
        Assert.Contains("Didn't make this change? Contact support immediately at support@rainsurance.example", email.Text);
    }

    [Fact]
    public void A_changed_address_is_told_it_has_been_changed_not_that_they_changed_it()
    {
        var email = Render("verify-after-change");

        Assert.Equal("Your RAINsurance email address has been changed", email.Subject);
        Assert.Contains("has been changed to j@example.com. Confirm this is you.", email.Text);
        Assert.Contains("You won't be able to enroll in any policies or file claims until you verify your email.", email.Text);
        Assert.DoesNotContain("You changed", email.Text);
        Assert.DoesNotContain("Thanks for creating an account", email.Text);
    }

    [Fact]
    public void Verification_links_say_how_many_minutes_they_last()
    {
        var e = new EmailVerificationRequested(1, "Jane Doe", "j@example.com", "t",
            new DateTime(2026, 11, 2, 19, 18, 0, DateTimeKind.Utc), ValidForMinutes: 15);

        Assert.Contains("expires in 15 minutes, at 2:18 PM EST", _templates.VerifyEmail(e).Text);
    }

    [Fact]
    public void A_declined_payment_says_when_it_was_attempted()
    {
        var e = new PaymentDeclined(7, "TXN-000007", 42, 10, 1, 4, 9m, new DateOnly(2026, 11, 2), "PayPal", "No.", false,
            new DateTime(2026, 11, 2, 19, 3, 0, DateTimeKind.Utc));

        Assert.Contains("November 2, 2026 at 2:03 PM EST", _templates.PaymentDeclined("Jane Doe", e, "Gold").Text);
    }

    [Fact]
    public void A_password_change_is_reported_with_a_warning()
    {
        var email = Render("password-changed");

        Assert.Equal("Your RAINsurance password was changed", email.Subject);
        Assert.Contains("Contact support immediately", email.Text);
    }

    // ---------------- Business time zone ----------------

    [Fact]
    public void Times_are_written_in_eastern_time()
    {
        // 03:30 UTC on Nov 3 is still Nov 2 in New York (EST, UTC-5); in July it's EDT (UTC-4).
        Assert.Equal("November 2, 2026 at 10:30 PM EST", EmailTemplates.DateTimeText(new DateTime(2026, 11, 3, 3, 30, 0, DateTimeKind.Utc)));
        Assert.Equal("July 4, 2026 at 8:00 AM EDT", EmailTemplates.DateTimeText(new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("November 2, 2026", EmailTemplates.Date(new DateTime(2026, 11, 3, 3, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Today_follows_the_business_time_zone_not_utc()
    {
        Assert.Equal(new DateOnly(2026, 11, 2), BusinessTime.TodayAt(new DateTimeOffset(2026, 11, 3, 3, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 3), BusinessTime.TodayAt(new DateTimeOffset(2026, 11, 3, 5, 30, 0, TimeSpan.Zero)));
        Assert.Equal("Eastern Standard Time", BusinessTime.SqlServerZoneName);
    }
}
