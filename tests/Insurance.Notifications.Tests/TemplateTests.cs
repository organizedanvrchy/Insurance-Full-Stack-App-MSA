using Insurance.Billing.Contracts;
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
        var e = new EmailVerificationRequested(1, "Jane Doe", "jane@example.com", "abc_DEF-123", EmailVerificationPurpose.Registration,
            new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc));

        var email = _templates.VerifyEmail(e);

        Assert.Equal("Verify your email address", email.Subject);
        Assert.Contains("http://app.test/verify-email?token=abc_DEF-123", email.Html);
        Assert.Contains("http://app.test/verify-email?token=abc_DEF-123", email.Text);
        Assert.Contains("November 3, 2026", email.Text);
        Assert.Contains("Hi Jane,", email.Text);
    }

    [Fact]
    public void Confirming_a_new_address_reads_differently()
    {
        var e = new EmailVerificationRequested(1, "Jane Doe", "new@example.com", "t", EmailVerificationPurpose.EmailChange, DateTime.UtcNow);

        var email = _templates.VerifyEmail(e);

        Assert.Equal("Confirm your new email address", email.Subject);
        Assert.Contains("new@example.com", email.Text);
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
        var e = new PaymentReceived(42, "PAY-00000042", 10, 1, 4, 89.99m, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2),
            DateTime.UtcNow, InstallmentNumber: 3, InstallmentCount: 12, new DateOnly(2026, 12, 1), 89.99m);

        var email = _templates.PaymentReceipt("Jane Doe", e, "Gold Health Plan");

        Assert.Equal("Payment receipt PAY-00000042", email.Subject);
        Assert.Contains("PAY-00000042", email.Text);
        Assert.Contains("$89.99", email.Text);
        Assert.Contains("3 of 12", email.Text);
        Assert.Contains("November 2, 2026", email.Text);                   // paid on
        Assert.Contains("$89.99 on December 1, 2026", email.Text);         // next payment
    }

    [Fact]
    public void The_last_receipt_says_the_policy_is_paid_up()
    {
        var e = new PaymentReceived(42, "PAY-00000042", 10, 1, 4, 89.99m, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2),
            DateTime.UtcNow, 12, 12, null, null);

        var email = _templates.PaymentReceipt("Jane Doe", e, "Gold Health Plan");

        Assert.Contains("fully paid up", email.Text);
        Assert.Contains("last installment", email.Text);
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
}
