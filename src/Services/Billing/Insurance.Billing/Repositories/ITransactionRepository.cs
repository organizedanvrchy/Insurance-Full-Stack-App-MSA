using Insurance.Billing.Contracts;
using Insurance.Billing.Enums;
using Insurance.Billing.Models;

namespace Insurance.Billing.Repositories;

public interface ITransactionRepository
{
    Task<PaymentTransaction?> GetByKeyAsync(int userId, string idempotencyKey);

    // Starts an attempt; Existing = true if this key was already used (a retry), and nothing was inserted.
    Task<(PaymentTransaction Transaction, bool Existing)> BeginAsync(PaymentTransaction attempt);

    // Records a Declined/Failed/Refunded outcome. With declinedEvent, the PaymentDeclined email event is saved in the
    // same database transaction (outbox), so it exists exactly when the outcome does.
    Task<PaymentTransaction> CompleteAsync(int transactionId, TransactionStatus status, string? providerChargeId,
        string? declineCode, string? message, PaymentDeclined? declinedEvent = null);

    // Marks the installment paid and the transaction Succeeded together, and saves the PaymentReceived event (the
    // emailed receipt) in the same database transaction. Throws PaymentConflict/PaymentNotFound.
    Task<Payment> SettleAsync(int paymentId, int transactionId, string providerChargeId, string message, ReceiptDetails receipt);

    Task<PaymentTransaction?> GetByIdAsync(int transactionId);
    Task<List<PaymentTransaction>> SearchAsync(int? enrollmentId, int? userId, TransactionStatus? status, int top = 200);
}

/// <summary>What the receipt email needs that the payment row doesn't have: whose it is, which plan, how it was paid.</summary>
public record ReceiptDetails(int UserId, int PolicyId, string PaymentMethod);
