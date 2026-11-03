using System.Globalization;
using Insurance.Billing.Contracts;
using Insurance.Claims.Contracts;
using Insurance.BuildingBlocks.Time;
using Insurance.Identity.Contracts;

namespace Insurance.Notifications.Templates;

/// <summary>
/// The text of every email the system sends. Pure functions of their inputs (no I/O), so each one is unit-tested.
/// Amounts are US dollars and dates are written out in full, as on the rest of the site.
/// </summary>
public class EmailTemplates
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private readonly AppLinks _links;

    public EmailTemplates(AppLinks links) => _links = links;

    // Every email says it's automated and where to get help.
    private EmailBuilder Email(string subject, string preheader) => new(subject, preheader, _links.SupportEmail);

    public static string Money(decimal amount) => amount.ToString("C", Us);
    public static string Date(DateOnly date) => date.ToString("MMMM d, yyyy", Us);

    /// <summary>A UTC timestamp as a date in the business time zone (see BusinessTime).</summary>
    public static string Date(DateTime utc) => BusinessTime.ToLocal(utc).ToString("MMMM d, yyyy", Us);

    /// <summary>"9:00 AM EST": the time of a UTC timestamp in the business time zone.</summary>
    public static string TimeText(DateTime utc)
    {
        var local = BusinessTime.ToLocal(utc);
        return $"{local.ToString("h:mm tt", Us)} {BusinessTime.Abbreviation(local)}";
    }

    /// <summary>"November 3, 2026 at 9:00 AM EST": a UTC timestamp in the business time zone.</summary>
    public static string DateTimeText(DateTime utc)
    {
        var local = BusinessTime.ToLocal(utc);
        return $"{local.ToString("MMMM d, yyyy 'at' h:mm tt", Us)} {BusinessTime.Abbreviation(local)}";
    }

    /// <summary>"Jane" from "Jane Doe". The whole name if it has no space.</summary>
    public static string FirstName(string fullName)
    {
        var trimmed = fullName.Trim();
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    // ---------------- Account ----------------

    public RenderedEmail VerifyEmail(EmailVerificationRequested e)
    {
        var link = _links.VerifyEmail(e.Token);
        const string Paused = "You won't be able to enroll in any policies or file claims until you verify your email.";

        // "This link works once and expires in 15 minutes, at 2:18 PM EST."
        var expires = e.ValidForMinutes > 0
            ? $"This link works once and expires in {e.ValidForMinutes} minutes, at {TimeText(e.ExpiresAtUtc)}."
            : $"This link works once and expires on {DateTimeText(e.ExpiresAtUtc)}.";
        var expired = "If it expires, resend another verification email.";

        // An administrator only changes the email when the customer asks, so this simply asks them to verify it.
        if (e.AfterEmailChange && e.ByAdmin)
            return Email("Verify your new RAINsurance email address", "Verify your new email address.")
                .Heading("Verify your new email address")
                .Paragraph($"Hi {FirstName(e.FullName)},")
                .Paragraph($"At your request, an administrator has changed the sign-in email for your RAINsurance account to {e.Email}. Please verify your new email address.")
                .Paragraph(Paused)
                .Button("Verify my email", link)
                .Small($"{expires} {expired}")
                .Small($"Didn't ask for this change? Contact us right away at {_links.SupportEmail}.")
                .Build();

        if (e.AfterEmailChange)
        {
            // Worded as something that happened to the account, not something the reader did: it may not have been them.
            var changed = $"The sign-in email for your RAINsurance account has been changed to {e.Email}. Confirm this is you.";
            return Email("Your RAINsurance email address has been changed", "Confirm this is you.")
                .Heading("Your email address has been changed")
                .Paragraph($"Hi {FirstName(e.FullName)},")
                .Paragraph(changed)
                .Paragraph(Paused)
                .Button("Confirm this is me", link)
                .Small($"{expires} {expired}")
                .Small($"Didn't expect this email? Contact us right away at {_links.SupportEmail}.")
                .Build();
        }

        return Email("Verify your email address", "Confirm your email to start enrolling in policies.")
            .Heading("Welcome to RAINsurance")
            .Paragraph($"Hi {FirstName(e.FullName)},")
            .Paragraph("Thanks for creating an account. Please confirm that this is your email address.")
            .Paragraph(Paused)
            .Button("Verify my email", link)
            .Small($"{expires} {expired}")
            .Small("Didn't create an account? You can safely ignore this email.")
            .Build();
    }

    /// <summary>To the OLD address: the only way the owner hears about it if someone else made the change.</summary>
    public RenderedEmail EmailChanged(EmailChanged e)
    {
        var email = Email("Your RAINsurance email address was changed", $"Your account now uses {e.NewEmail}.")
            .Heading("Your email address was changed")
            .Paragraph($"Hi {FirstName(e.FullName)},");

        if (e.ByAdmin)
            return email
                .Paragraph($"At your request, an administrator changed the sign-in email for your RAINsurance account from {e.OldEmail} to {e.NewEmail} on {DateTimeText(e.ChangedAtUtc)}. We'll send everything to the new address from now on.")
                .Paragraph("You'll have to verify the new email address before you can enroll in policies or file claims. We've sent a link to it.")
                .Callout("Didn't ask for this change?", $"Contact support immediately at {_links.SupportEmail}.", warning: true)
                .Build();

        return email
            .Paragraph($"The sign-in email for your RAINsurance account was changed from {e.OldEmail} to {e.NewEmail} on {DateTimeText(e.ChangedAtUtc)}. We'll send everything to the new address from now on.")
            .Paragraph("If you made this change, you can ignore this email.")
            .Callout("Didn't make this change?", $"Contact support immediately at {_links.SupportEmail}. Someone else may have access to your account.", warning: true)
            .Build();
    }

    /// <summary>An administrator reset the password: the one-time password to sign in with, and how long it works.</summary>
    public RenderedEmail PasswordResetByAdmin(PasswordResetByAdmin e) =>
        Email("Your RAINsurance password has been reset", "Your one-time password is inside.")
            .Heading("Your password has been reset")
            .Paragraph($"Hi {FirstName(e.FullName)},")
            .Paragraph("An administrator has reset the password for your RAINsurance account. Your previous password no longer works.")
            .Code("Your one-time password", e.OneTimePassword)
            .Paragraph("Enter this one-time password the next time you sign in. You'll then be asked to choose a new password.")
            .Callout("Use it soon",
                $"This one-time password expires in {e.ValidForMinutes} minutes, at {TimeText(e.ExpiresAtUtc)}. After that, contact us to have a new one sent.")
            .Button("Sign in", _links.SignIn)
            .Small($"Didn't expect this email? Contact us right away at {_links.SupportEmail}.")
            .Build();

    public RenderedEmail PasswordChanged(PasswordChanged e) =>
        Email("Your RAINsurance password was changed", "Your password was just changed.")
            .Heading("Your password was changed")
            .Paragraph($"Hi {FirstName(e.FullName)},")
            .Paragraph($"The password for your RAINsurance account was changed on {DateTimeText(e.ChangedAtUtc)}.")
            .Paragraph("If you made this change, you can ignore this email.")
            .Callout("Didn't make this change?", $"Contact support immediately at {_links.SupportEmail}. Someone else may have access to your account.", warning: true)
            .Build();

    // ---------------- Policies ----------------

    public RenderedEmail PolicyActivated(string fullName, PolicySummary policy) =>
        Email($"Your {policy.PolicyName} policy is active", $"Coverage starts {Date(policy.StartDate)}.")
            .Heading("You're covered")
            .Paragraph($"Hi {FirstName(fullName)},")
            .Paragraph($"Your enrollment in {policy.PolicyName} is complete. Here are the details for your records.")
            .Details(
                ("Policy", policy.PolicyName),
                ("Coverage", Money(policy.CoverageAmount)),
                ("Coverage period", $"{Date(policy.StartDate)} – {Date(policy.EndDate)}"),
                ("First premium due", Date(policy.StartDate)))
            .Button("View my policies", _links.MyPolicies)
            .Build();

    public RenderedEmail PolicyCancelled(string fullName, PolicySummary policy) =>
        Email($"Your {policy.PolicyName} policy was cancelled", "Your cancellation is confirmed.")
            .Heading("Policy cancelled")
            .Paragraph($"Hi {FirstName(fullName)},")
            .Paragraph($"As requested, your {policy.PolicyName} policy has been cancelled. Any premiums you hadn't paid yet have been removed; the ones you paid stay in your payment history.")
            .Small("If you didn't cancel this policy, please contact us.")
            .Button("View my policies", _links.MyPolicies)
            .Build();

    // ---------------- Claims ----------------

    /// <summary>A document on the claim, and whether a copy is attached to this email.</summary>
    public record ClaimDocumentCopy(string FileName, string ContentType, int SizeBytes, bool Attached);

    /// <summary>The customer's copy of their claim, with the documents they added (copies attached where they fit).</summary>
    public RenderedEmail ClaimSubmitted(string fullName, ClaimSubmitted e, string policyName,
        IReadOnlyList<ClaimDocumentCopy>? documents = null)
    {
        documents ??= Array.Empty<ClaimDocumentCopy>();

        var email = Email($"We received your claim {e.ClaimNumber}", "Your claim is under review.")
            .Heading("Claim received")
            .Paragraph($"Hi {FirstName(fullName)},")
            .Paragraph("Thanks, we've received your claim. Your claim is under review and we'll email you when a decision has been made. Please allow at least 5-7 business days for a response.")
            .Details(
                ("Claim number", e.ClaimNumber),
                ("Policy", policyName),
                ("Incident date", Date(e.IncidentDate)),
                ("Amount claimed", Money(e.ClaimAmount)),
                ("Status", "Submitted"));

        if (documents.Count == 0)
        {
            email.Small("No documents were added to this claim. You can add them from My claims until a decision has been made.");
        }
        else
        {
            email.Paragraph(documents.Count == 1 ? "Document added to this claim:" : $"Documents added to this claim ({documents.Count}):")
                .Details(documents.Select(d => (d.FileName, $"{DocumentType(d.ContentType)}, {FileSize(d.SizeBytes)}")).ToArray());

            if (documents.All(d => d.Attached))
                email.Small(documents.Count == 1 ? "A copy is attached to this email." : "Copies are attached to this email.");
            else if (documents.Any(d => d.Attached))
                email.Small("Copies are attached, except for files too large to email. You can download every document from My claims.");
            else
                email.Small("The files are too large to email. You can download them from My claims.");
        }

        return email.Button("Track my claim", _links.MyClaims).Build();
    }

    public static string DocumentType(string contentType) => contentType switch
    {
        "application/pdf" => "PDF",
        "image/jpeg" => "JPG image",
        "image/png" => "PNG image",
        _ => "File"
    };

    public static string FileSize(int bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{Math.Round(bytes / 1024.0)} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB"
    };

    /// <summary>The decision on a claim (approved or rejected, with the reviewer's reason or notes), and the payment.</summary>
    public RenderedEmail ClaimStatusChanged(string fullName, ClaimStatusChanged e, string policyName)
    {
        var (subject, heading, message) = e.NewStatus switch
        {
            ClaimStatus.Approved => ($"Good news: claim {e.ClaimNumber} was approved", "Your claim was approved",
                $"Your claim has been processed and approved. Your payment of {Money(e.ClaimAmount)} will be sent out in about 2-3 business days, and we'll email you as soon as it's on its way."),
            ClaimStatus.Rejected => ($"An update on your claim {e.ClaimNumber}", "We couldn't approve your claim",
                "We apologize, but after reviewing your claim, we've decided that we're unable to approve it. The reviewer's reason is below."),
            ClaimStatus.NeedsMoreInfo => ($"We need more information about claim {e.ClaimNumber}", "We need more information about your claim",
                "Our claims department needs more information about your claim before they can make a decision. Their questions are below."),
            ClaimStatus.Paid => ($"Your payment for claim {e.ClaimNumber} is on its way", "Your claim payment has been sent",
                $"We've sent your payment of {Money(e.ClaimAmount)} for this claim. You'll receive your check in about 5-10 business days."),
            _ => ($"Your claim {e.ClaimNumber} was updated", "Your claim was updated", "The status of your claim has changed.")
        };

        var email = Email(subject, message)
            .Heading(heading)
            .Paragraph($"Hi {FirstName(fullName)},")
            .Paragraph(message)
            .Details(
                ("Claim number", e.ClaimNumber),
                ("Policy", policyName),
                ("Amount claimed", Money(e.ClaimAmount)),
                ("Status", StatusLabel(e.NewStatus)),
                ("Updated", Date(e.ChangedAtUtc)));

        if (!string.IsNullOrWhiteSpace(e.Notes))
            email.Callout(e.NewStatus switch
                {
                    ClaimStatus.Rejected => "Reason",
                    ClaimStatus.NeedsMoreInfo => "Questions from the claims department",
                    _ => "Reviewer's notes"
                }, e.Notes.Trim(), warning: e.NewStatus == ClaimStatus.Rejected);

        if (e.NewStatus == ClaimStatus.Rejected)
            email.Paragraph($"If you'd like to dispute this decision, contact us at {_links.SupportEmail}.");

        if (e.NewStatus == ClaimStatus.NeedsMoreInfo)
            email.Paragraph("We recommend adding images or documents that give more evidence, and answering the questions above. You can add them to the claim from My claims, then choose \"Send to reviewer\" when you're done.");

        return email.Button("View my claims", _links.MyClaims).Build();
    }

    public static string StatusLabel(ClaimStatus status) => status switch
    {
        ClaimStatus.UnderReview => "Under review",
        ClaimStatus.NeedsMoreInfo => "Needs more information",
        _ => status.ToString()
    };

    // ---------------- Payments ----------------

    public RenderedEmail PaymentReceipt(string fullName, PaymentReceived e, string policyName)
    {
        var email = Email($"Payment receipt {e.ReceiptNumber}", $"We received your {Money(e.Amount)} payment for {policyName}.")
            .Heading("Thanks for your payment")
            .Paragraph($"Hi {FirstName(fullName)},")
            .Paragraph($"We've received your premium payment for {policyName}. Keep this email as your receipt.")
            .Details(
                ("Receipt number", e.ReceiptNumber),
                ("Policy", policyName),
                ("Amount paid", Money(e.Amount)),
                ("Paid with", e.PaymentMethod),
                ("Paid on", DateTimeText(e.PaidAtUtc)),
                ("Installment", $"{e.InstallmentNumber} of {e.InstallmentCount}"),
                ("Covers premium due", Date(e.DueDate)),
                ("Next payment", e.NextDueDate is { } next ? $"{Money(e.NextDueAmount ?? 0)} on {Date(next)}" : "None, you're fully paid up"));

        if (e.NextDueDate is null)
            email.Paragraph("That was the last installment on this policy. Thank you!");

        return email.Button("View receipt", _links.Receipt(e.TransactionId)).Build();
    }

    public RenderedEmail PaymentDeclined(string fullName, PaymentDeclined e, string policyName)
    {
        var amount = Money(e.Amount);
        var next = e.CanRetry
            ? "This was a problem on the payment processor's side, not with your account. Please try again in a few minutes."
            : "Please check the details or use a different payment method.";

        return Email($"Your {policyName} payment didn't go through", $"Your {amount} payment was not completed. Nothing was charged.")
            .Heading("Your payment didn't go through")
            .Paragraph($"Hi {FirstName(fullName)},")
            .Paragraph($"We couldn't complete your {amount} premium payment for {policyName}. You have not been charged, and the installment is still unpaid.")
            .Callout("Reason", e.Reason, warning: true)
            .Details(
                ("Reference", e.Reference),
                ("Policy", policyName),
                ("Amount", amount),
                ("Payment method", e.PaymentMethod),
                ("Premium due", Date(e.DueDate)),
                ("Attempted", DateTimeText(e.AttemptedAtUtc)))
            .Paragraph(next)
            .Button("Try again", _links.PaymentSchedule(e.EnrollmentId))
            .Small("Didn't try to make a payment? Please contact us.")
            .Build();
    }

    public RenderedEmail PaymentReminder(string fullName, PaymentReminderDue e, string policyName)
    {
        var amount = Money(e.Amount);
        var due = Date(e.DueDate);
        var (subject, heading, message, warning) = e.Stage switch
        {
            PaymentReminderStage.DueSoon => (
                $"Your {policyName} premium is due on {due}", "Your premium is due soon",
                $"Just a heads-up: your {amount} premium for {policyName} is due on {due}.", false),
            PaymentReminderStage.DueToday => (
                $"Your {policyName} premium is due today", "Your premium is due today",
                $"Your {amount} premium for {policyName} is due today, {due}.", false),
            PaymentReminderStage.Overdue => (
                $"Your {policyName} premium is overdue", "Your premium is overdue",
                $"We haven't received your {amount} premium for {policyName}, which was due on {due}.", true),
            PaymentReminderStage.OverdueSecondNotice => (
                $"Reminder: your {policyName} premium is {e.DaysPastDue} days overdue", "Your premium is still overdue",
                $"Your {amount} premium for {policyName} was due on {due} and is now {e.DaysPastDue} days overdue.", true),
            _ => (
                $"Final notice: your {policyName} premium is {e.DaysPastDue} days overdue", "Final notice: please pay your premium",
                $"Your {amount} premium for {policyName} was due on {due} and is now {e.DaysPastDue} days overdue. This is our last reminder for this installment.", true)
        };

        var email = Email(subject, message)
            .Heading(heading)
            .Paragraph($"Hi {FirstName(fullName)},")
            .Paragraph(message)
            .Details(("Policy", policyName), ("Amount", amount), ("Due date", due));

        if (warning)
            email.Callout("While a premium is overdue", "You can't file new claims on this policy until it's paid. Paying takes less than a minute.", warning: true);
        else
            email.Small("Already paid? Thank you, you can ignore this reminder.");

        return email
            .Button("Pay now", _links.PaymentSchedule(e.EnrollmentId))
            .FooterNote("Don't want payment reminders?", "Change your email settings", _links.EmailSettings)
            .Build();
    }
}

/// <summary>What the policy emails show, gathered from the Enrollments and Catalog services.</summary>
public record PolicySummary(string PolicyName, decimal CoverageAmount, DateOnly StartDate, DateOnly EndDate);
