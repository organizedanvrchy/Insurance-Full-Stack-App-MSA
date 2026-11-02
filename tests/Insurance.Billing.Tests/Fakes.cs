using Insurance.Billing.Contracts;
using Insurance.Billing.Enums;
using Insurance.Billing.Exceptions;
using Insurance.Billing.Gateway;
using Insurance.Billing.Models;
using Insurance.Billing.Repositories;
using Insurance.BuildingBlocks.Security;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Insurance.Billing.Tests;

// Fakes are tiny in-memory stand-ins for the 
// real repositories and for the other services' contracts.
// The services only know the interfaces, so they 
// can't tell the difference. That's dependency injection 
// paying off: no database, no Azure, tests run in milliseconds.

public class FakePaymentRepository : IPaymentRepository
{
    public List<Payment> Payments { get; } = new();

    public Task<List<Payment>> GetByEnrollmentAsync(int enrollmentId) =>
        Task.FromResult(Payments.Where(p => p.EnrollmentId == enrollmentId).OrderBy(p => p.DueDate).ToList());

    public Task<Payment?> GetByIdAsync(int paymentId) =>
        Task.FromResult(Payments.FirstOrDefault(p => p.PaymentId == paymentId));

    public Task CreateScheduleAsync(int enrollmentId, DateOnly startDate, int termMonths, decimal monthlyPremium) =>
        throw new NotSupportedException();

    public Task RemoveUnpaidAsync(int enrollmentId) => throw new NotSupportedException();

    public Task<Dictionary<int, PremiumSummary>> GetSummariesAsync(IEnumerable<int> enrollmentIds) =>
        throw new NotSupportedException();
}

public class FakeTransactionRepository : ITransactionRepository
{
    private readonly FakePaymentRepository _payments;
    public FakeTransactionRepository(FakePaymentRepository payments) => _payments = payments;

    public List<PaymentTransaction> Transactions { get; } = new();

    // Simulates another payment winning the race between the checks and settling.
    public Exception? SettleThrows { get; set; }

    public Task<PaymentTransaction?> GetByKeyAsync(int userId, string idempotencyKey) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.UserId == userId && t.IdempotencyKey == idempotencyKey));

    public Task<(PaymentTransaction Transaction, bool Existing)> BeginAsync(PaymentTransaction attempt)
    {
        var existing = Transactions.FirstOrDefault(t => t.UserId == attempt.UserId && t.IdempotencyKey == attempt.IdempotencyKey);
        if (existing is not null) return Task.FromResult((existing, true));

        attempt.TransactionId = Transactions.Count + 1;
        attempt.Status = TransactionStatus.Processing;
        attempt.CreatedAt = DateTime.UtcNow;
        Transactions.Add(attempt);
        return Task.FromResult((attempt, false));
    }

    public Task<PaymentTransaction> CompleteAsync(int transactionId, TransactionStatus status, string? providerChargeId,
        string? declineCode, string? message)
    {
        var t = Transactions.Single(x => x.TransactionId == transactionId);
        t.Status = status;
        t.ProviderChargeId = providerChargeId ?? t.ProviderChargeId;
        t.DeclineCode = declineCode;
        t.Message = message;
        t.CompletedAt = DateTime.UtcNow;
        return Task.FromResult(t);
    }

    public Task<Payment> SettleAsync(int paymentId, int transactionId, string providerChargeId, string message)
    {
        if (SettleThrows is not null) throw SettleThrows;

        var payment = _payments.Payments.Single(p => p.PaymentId == paymentId);
        if (payment.PaidDate is not null) throw new PaymentConflictException("This installment has already been paid.");
        payment.PaidDate = DateOnly.FromDateTime(DateTime.UtcNow);
        payment.Status = PaymentStatus.Paid;

        var t = Transactions.Single(x => x.TransactionId == transactionId);
        t.Status = TransactionStatus.Succeeded;
        t.ProviderChargeId = providerChargeId;
        t.Message = message;
        return Task.FromResult(payment);
    }

    public Task<PaymentTransaction?> GetByIdAsync(int transactionId) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.TransactionId == transactionId));

    public Task<List<PaymentTransaction>> SearchAsync(int? enrollmentId, int? userId, TransactionStatus? status, int top = 200) =>
        Task.FromResult(Transactions
            .Where(t => (enrollmentId == null || t.EnrollmentId == enrollmentId) && (userId == null || t.UserId == userId)
                        && (status == null || t.Status == status))
            .OrderByDescending(t => t.TransactionId).Take(top).ToList());
}

public class FakeSavedMethodRepository : ISavedMethodRepository
{
    public List<SavedPaymentMethod> Methods { get; } = new();

    public Task<List<SavedPaymentMethod>> GetByUserAsync(int userId) =>
        Task.FromResult(Methods.Where(m => m.UserId == userId).ToList());

    public Task<SavedPaymentMethod?> GetByIdAsync(int paymentMethodId) =>
        Task.FromResult(Methods.FirstOrDefault(m => m.PaymentMethodId == paymentMethodId));

    public Task AddAsync(SavedPaymentMethod method)
    {
        method.PaymentMethodId = Methods.Count + 1;
        Methods.Add(method);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(int paymentMethodId, int userId) =>
        Task.FromResult(Methods.RemoveAll(m => m.PaymentMethodId == paymentMethodId && m.UserId == userId) > 0);
}

// The real mock processor (no artificial delay), counting what is asked of it.
public class CountingGateway : IPaymentGateway
{
    private readonly MockPaymentGateway _inner = new(Options.Create(new MockGatewayOptions { LatencyMs = 0 }));

    public int Charges { get; private set; }
    public List<string> Refunds { get; } = new();

    public string Name => _inner.Name;
    public Task<PaymentMethodDetails> DescribeAsync(string token) => _inner.DescribeAsync(token);

    public Task<ChargeResult> ChargeAsync(ChargeRequest request)
    {
        Charges++;
        return _inner.ChargeAsync(request);
    }

    public Task<string> SaveForReuseAsync(string token, string customer) => _inner.SaveForReuseAsync(token, customer);
    public Task RemoveSavedAsync(string providerMethodId) => _inner.RemoveSavedAsync(providerMethodId);

    public Task<RefundResult> RefundAsync(string chargeId, decimal amount, string idempotencyKey)
    {
        Refunds.Add(chargeId);
        return _inner.RefundAsync(chargeId, amount, idempotencyKey);
    }
}

public class FakeEnrollmentLookup : IEnrollmentLookup
{
    public Dictionary<int, EnrollmentInfo> Enrollments { get; } = new();

    public Task<EnrollmentInfo?> GetAsync(int enrollmentId) =>
        Task.FromResult(Enrollments.GetValueOrDefault(enrollmentId));

    public Task<IReadOnlyDictionary<int, EnrollmentInfo>> GetManyAsync(IEnumerable<int> enrollmentIds) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<int, int>> CountActiveByUserAsync(IEnumerable<int> userIds) =>
        throw new NotSupportedException();
}

public class FakeUserDirectory : IUserDirectory
{
    public Dictionary<int, UserAccount> Users { get; } = new()
    {
        [1] = new UserAccount(1, "Cal Tester", "cal@test.example", UserRole.Customer, true),
        [2] = new UserAccount(2, "Dee Tester", "dee@test.example", UserRole.Customer, true),
    };

    public Task<UserAccount?> GetAsync(int userId) => Task.FromResult(Users.GetValueOrDefault(userId));

    public Task<IReadOnlyDictionary<int, UserAccount>> GetManyAsync(IEnumerable<int> userIds) =>
        Task.FromResult<IReadOnlyDictionary<int, UserAccount>>(
            Users.Where(u => userIds.Contains(u.Key)).ToDictionary(u => u.Key, u => u.Value));
}
