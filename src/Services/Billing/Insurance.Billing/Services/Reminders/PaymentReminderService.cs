using Insurance.Billing.Contracts;
using Insurance.Billing.Repositories;
using Insurance.Enrollments.Contracts;
using Microsoft.Extensions.Logging;

namespace Insurance.Billing.Services.Reminders;

public interface IPaymentReminderService
{
    /// <summary>Publishes today's reminders. Returns how many were published.</summary>
    Task<int> SendDueRemindersAsync(DateOnly today, CancellationToken ct = default);
}

// Decides which reminders are due (ReminderSchedule), checks the policies are still in force (Enrollments service,
// HTTP, with Billing's own service token), and records each reminder with its PaymentReminderDue event in one
// transaction. Whether the customer wants reminders is up to the Notifications service, which owns preferences.
public class PaymentReminderService : IPaymentReminderService
{
    private readonly IPaymentRepository _payments;
    private readonly IEnrollmentLookup _enrollments;
    private readonly ILogger<PaymentReminderService> _logger;

    public PaymentReminderService(IPaymentRepository payments, IEnrollmentLookup enrollments, ILogger<PaymentReminderService> logger)
    {
        _payments = payments;
        _enrollments = enrollments;
        _logger = logger;
    }

    public async Task<int> SendDueRemindersAsync(DateOnly today, CancellationToken ct = default)
    {
        var (from, to) = ReminderSchedule.CandidateWindow(today);
        var due = (await _payments.GetReminderCandidatesAsync(from, to))
            .Select(c => (Candidate: c, Stage: ReminderSchedule.StageToSend(c, today)))
            .Where(x => x.Stage is not null)
            .ToList();

        if (due.Count == 0) return 0;

        var enrollments = await _enrollments.GetManyAsync(due.Select(x => x.Candidate.EnrollmentId));
        var sent = 0;

        foreach (var (c, stage) in due)
        {
            ct.ThrowIfCancellationRequested();

            // Cancelled policies lose their unpaid installments a moment after the cancellation; until then, skip them.
            // Expired policies can still owe their last installments, so they are reminded too.
            if (!enrollments.TryGetValue(c.EnrollmentId, out var e) ||
                e.Status is not (EnrollmentStatus.Active or EnrollmentStatus.Expired))
                continue;

            var reminder = new PaymentReminderDue(
                c.PaymentId, c.EnrollmentId, e.UserId, e.PolicyId, c.DueDate, c.Amount, stage!.Value,
                DaysPastDue: today.DayNumber - c.DueDate.DayNumber);

            if (await _payments.RecordReminderAsync(reminder))
                sent++;
        }

        if (sent > 0)
            _logger.LogInformation("Queued {Count} payment reminder(s) for {Today}.", sent, today);
        return sent;
    }
}
