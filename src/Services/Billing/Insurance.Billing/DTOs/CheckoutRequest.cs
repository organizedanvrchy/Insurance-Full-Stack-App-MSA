using System.ComponentModel.DataAnnotations;

namespace Insurance.Billing.DTOs;

// The body of POST /api/payments/{id}/pay. Choose exactly one of PaymentMethodToken or SavedPaymentMethodId.
// There is deliberately no field for a card number or security code: those never leave the browser.
public class CheckoutRequest
{
    // One-time token from the payment provider's browser library (tok_...).
    [StringLength(200)]
    public string? PaymentMethodToken { get; set; }

    // Or one of the customer's saved methods.
    public int? SavedPaymentMethodId { get; set; }

    // A new random value for every attempt, made by the browser. Sending it again (a retry after a timeout)
    // returns the first attempt's result instead of charging twice.
    [Required, StringLength(100, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;

    // Keep this method for next time (only for a newly entered method).
    public bool SaveMethod { get; set; }

    [StringLength(100)]
    public string? BillingName { get; set; }

    [StringLength(10)]
    public string? BillingPostalCode { get; set; }
}
