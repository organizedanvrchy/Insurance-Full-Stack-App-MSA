using Insurance.Billing.Contracts;
using Insurance.Billing.Models;
using Insurance.Billing.Services.Reminders;

namespace Insurance.Billing.Repositories;

public interface IPaymentRepository
{
    Task<List<Payment>> GetByEnrollmentAsync(int enrollmentId);
    Task<Payment?> GetByIdAsync(int paymentId);

    Task CreateScheduleAsync(int enrollmentId, DateOnly startDate, int termMonths, decimal monthlyPremium);
    Task RemoveUnpaidAsync(int enrollmentId);
    Task<Dictionary<int, PremiumSummary>> GetSummariesAsync(IEnumerable<int> enrollmentIds);

    // Payment reminders (Services/Reminders)
    Task<List<ReminderCandidate>> GetReminderCandidatesAsync(DateOnly fromDueDate, DateOnly toDueDate);

    // Records the reminder and saves its event (outbox) in one transaction. false => already sent, or paid meanwhile.
    Task<bool> RecordReminderAsync(PaymentReminderDue reminder);
}
