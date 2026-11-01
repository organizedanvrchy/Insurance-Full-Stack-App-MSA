using Insurance.Billing.Enums;

namespace Insurance.Billing.DTOs;

// What the checkout page shows after "Pay": a receipt, or why the payment was refused.
public class CheckoutResultDto
{
    public CheckoutOutcome Outcome { get; set; }
    public string Message { get; set; } = string.Empty;
    public TransactionDto Transaction { get; set; } = new();
    public PaymentDto Payment { get; set; } = new();       // the installment, Paid after a success
}
