using Insurance.Billing.Enums;

namespace Insurance.Billing.Models;

// A payment method the customer chose to keep (billing.SavedPaymentMethods).
// ProviderMethodId is the processor's reusable reference, never a card number.
public class SavedPaymentMethod
{
    public int PaymentMethodId { get; set; }
    public int UserId { get; set; }
    public PaymentMethodType MethodType { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? HolderName { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderMethodId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
