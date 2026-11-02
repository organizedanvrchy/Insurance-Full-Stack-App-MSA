using Insurance.Billing.Enums;

namespace Insurance.Billing.DTOs;

// What the checkout page shows for a saved method: never the processor's reference.
public class SavedPaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodType MethodType { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public string? HolderName { get; set; }
    public DateTime CreatedAt { get; set; }
}
