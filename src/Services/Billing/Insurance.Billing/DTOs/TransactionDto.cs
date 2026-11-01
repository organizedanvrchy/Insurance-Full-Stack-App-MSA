using Insurance.Billing.Enums;

namespace Insurance.Billing.DTOs;

// A line in the payment log, and the content of a receipt.
public class TransactionDto
{
    public int TransactionId { get; set; }
    public string Reference { get; set; } = string.Empty;   // TXN-000042: what the customer quotes to support
    public int PaymentId { get; set; }
    public int EnrollmentId { get; set; }
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
    public string? BillingName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Filled only in the admin payment log (names come from the Identity service).
    public int? UserId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}
