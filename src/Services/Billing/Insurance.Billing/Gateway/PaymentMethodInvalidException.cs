using Insurance.BuildingBlocks.Exceptions;

namespace Insurance.Billing.Gateway;

/// <summary>The payment method token is missing, malformed or expired at the processor. Becomes a 400.</summary>
public class PaymentMethodInvalidException : BusinessRuleException
{
    public PaymentMethodInvalidException(string message) : base(message) { }
}
