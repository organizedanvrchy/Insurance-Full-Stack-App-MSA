using Insurance.Billing.Contracts;
using Insurance.Billing.Enums;
using Insurance.Billing.Models;
using Insurance.Billing.Repositories;
using Insurance.Billing.Services.Reminders;
using Insurance.Enrollments.Contracts;

namespace Insurance.Billing.Tests;

// Fakes are tiny in-memory stand-ins for the 
// real repositories and for the other services' contracts.
// The services only know the interfaces, so they 
// can't tell the difference. That's dependency injection 
// paying off: no database, no Azure, tests run in milliseconds.

public class FakePaymentRepository : IPaymentRepository
{
    public List<Payment> Payments { get; } = new();
    public int? LastPaid { get; private set; }

    public Task<List<Payment>> GetByEnrollmentAsync(int enrollmentId) =>
        Task.FromResult(Payments.Where(p => p.EnrollmentId == enrollmentId).OrderBy(p => p.DueDate).ToList());

    public Task<Payment?> GetByIdAsync(int paymentId) =>
        Task.FromResult(Payments.FirstOrDefault(p => p.PaymentId == paymentId));

    // What PayAsync would have written to the outbox for the receipt: (paymentId, userId, policyId).
    public List<(int PaymentId, int UserId, int PolicyId)> Receipts { get; } = new();

    public Task<Payment> PayAsync(int paymentId, int userId, int policyId)
    {
        LastPaid = paymentId;
        Receipts.Add((paymentId, userId, policyId));
        var payment = Payments.Single(p => p.PaymentId == paymentId);
        payment.PaidDate = DateOnly.FromDateTime(DateTime.UtcNow);
        payment.Status = PaymentStatus.Paid;
        return Task.FromResult(payment);
    }

    public Task CreateScheduleAsync(int enrollmentId, DateOnly startDate, int termMonths, decimal monthlyPremium) =>
        throw new NotSupportedException();

    public Task RemoveUnpaidAsync(int enrollmentId) => throw new NotSupportedException();

    public Task<Dictionary<int, PremiumSummary>> GetSummariesAsync(IEnumerable<int> enrollmentIds) =>
        throw new NotSupportedException();

    // ---- Payment reminders ----

    public List<ReminderCandidate> Candidates { get; } = new();
    public (DateOnly From, DateOnly To)? LastCandidateWindow { get; private set; }
    public List<PaymentReminderDue> Recorded { get; } = new();   // what RecordReminderAsync would have published

    public Task<List<ReminderCandidate>> GetReminderCandidatesAsync(DateOnly fromDueDate, DateOnly toDueDate)
    {
        LastCandidateWindow = (fromDueDate, toDueDate);
        return Task.FromResult(Candidates.Where(c => c.DueDate >= fromDueDate && c.DueDate <= toDueDate).ToList());
    }

    // Like usp_Reminder_Record: once per (installment, stage).
    public Task<bool> RecordReminderAsync(PaymentReminderDue reminder)
    {
        if (Recorded.Any(r => r.PaymentId == reminder.PaymentId && r.Stage == reminder.Stage))
            return Task.FromResult(false);
        Recorded.Add(reminder);
        return Task.FromResult(true);
    }
}

public class FakeEnrollmentLookup : IEnrollmentLookup
{
    public Dictionary<int, EnrollmentInfo> Enrollments { get; } = new();

    public Task<EnrollmentInfo?> GetAsync(int enrollmentId) =>
        Task.FromResult(Enrollments.GetValueOrDefault(enrollmentId));

    public Task<IReadOnlyDictionary<int, EnrollmentInfo>> GetManyAsync(IEnumerable<int> enrollmentIds) =>
        Task.FromResult<IReadOnlyDictionary<int, EnrollmentInfo>>(
            Enrollments.Where(e => enrollmentIds.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value));

    public Task<IReadOnlyDictionary<int, int>> CountActiveByUserAsync(IEnumerable<int> userIds) =>
        throw new NotSupportedException();
}
