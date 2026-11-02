using Insurance.Billing.Enums;

namespace Insurance.Billing.Models;

// One attempt to pay an installment (billing.PaymentTransactions). Never holds card numbers.
public class PaymentTransaction
{
    public int TransactionId { get; set; }
    public int PaymentId { get; set; }
    public int EnrollmentId { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentMethodType MethodType { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderChargeId { get; set; }
    public TransactionStatus Status { get; set; }
    public string? DeclineCode { get; set; }
    public string? Message { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? BillingName { get; set; }
    public string? BillingPostalCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
