using Insurance.Billing.Contracts;
using Insurance.Billing.Models;
using Insurance.Billing.Services.Reminders;

namespace Insurance.Billing.Repositories;

public interface IPaymentRepository
{
    Task<List<Payment>> GetByEnrollmentAsync(int enrollmentId);
    Task<Payment?> GetByIdAsync(int paymentId);
    // Pays and saves the PaymentReceived event (outbox) in one transaction. Throws PaymentNotFound / PaymentConflict.
    // userId and policyId come from the Enrollments service and go into the receipt event.
    Task<Payment> PayAsync(int paymentId, int userId, int policyId);

    Task CreateScheduleAsync(int enrollmentId, DateOnly startDate, int termMonths, decimal monthlyPremium);
    Task RemoveUnpaidAsync(int enrollmentId);
    Task<Dictionary<int, PremiumSummary>> GetSummariesAsync(IEnumerable<int> enrollmentIds);

    // Payment reminders (Services/Reminders)
    Task<List<ReminderCandidate>> GetReminderCandidatesAsync(DateOnly fromDueDate, DateOnly toDueDate);

    // Records the reminder and saves its event (outbox) in one transaction. false => already sent, or paid meanwhile.
    Task<bool> RecordReminderAsync(PaymentReminderDue reminder);
}
