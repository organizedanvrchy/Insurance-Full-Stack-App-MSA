using Insurance.Billing.Contracts;
using Insurance.Billing.Models;

namespace Insurance.Billing.Repositories;

public interface IPaymentRepository
{
    Task<List<Payment>> GetByEnrollmentAsync(int enrollmentId);
    Task<Payment?> GetByIdAsync(int paymentId);

    Task CreateScheduleAsync(int enrollmentId, DateOnly startDate, int termMonths, decimal monthlyPremium);
    Task RemoveUnpaidAsync(int enrollmentId);
    Task<Dictionary<int, PremiumSummary>> GetSummariesAsync(IEnumerable<int> enrollmentIds);
}
