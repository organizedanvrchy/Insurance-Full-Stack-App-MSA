using Insurance.Billing.Enums;
using Insurance.Billing.Models;

namespace Insurance.Billing.Repositories;

public interface ITransactionRepository
{
    Task<PaymentTransaction?> GetByKeyAsync(int userId, string idempotencyKey);

    // Starts an attempt; Existing = true if this key was already used (a retry), and nothing was inserted.
    Task<(PaymentTransaction Transaction, bool Existing)> BeginAsync(PaymentTransaction attempt);

    Task<PaymentTransaction> CompleteAsync(int transactionId, TransactionStatus status, string? providerChargeId,
        string? declineCode, string? message);

    // Marks the installment paid and the transaction Succeeded together. Throws PaymentConflict/PaymentNotFound.
    Task<Payment> SettleAsync(int paymentId, int transactionId, string providerChargeId, string message);

    Task<PaymentTransaction?> GetByIdAsync(int transactionId);
    Task<List<PaymentTransaction>> SearchAsync(int? enrollmentId, int? userId, TransactionStatus? status, int top = 200);
}
