using Insurance.Billing.Enums;

namespace Insurance.Billing.Gateway;

/// <summary>
/// Everything this service needs from a payment processor. The rest of the service only knows this interface,
/// so moving from the simulation to a real processor (Stripe, Braintree, Adyen...) means writing one new class
/// that implements it and changing "Payments:Provider" in the configuration.
///
/// Card details never reach this service. The browser sends them straight to the processor, which answers with a
/// one-time token ("tok_..."); that token is all we receive. A saved method is a reusable reference ("pm_...").
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Stored with every transaction, so you can tell which processor handled it.</summary>
    string Name { get; }

    /// <summary>What a token stands for (type, brand, last 4 digits, expiry), without charging it.
    /// Throws <see cref="PaymentMethodInvalidException"/> if the token can't be used.</summary>
    Task<PaymentMethodDetails> DescribeAsync(string paymentMethodToken);

    /// <summary>Takes the money. A decline is a normal result, not an exception. Sending the same idempotency key
    /// again returns the first result instead of charging twice.</summary>
    Task<ChargeResult> ChargeAsync(ChargeRequest request);

    /// <summary>Turns a one-time token into a reusable reference for this customer ("save this card").</summary>
    Task<string> SaveForReuseAsync(string paymentMethodToken, string customerReference);

    /// <summary>Removes a saved method at the processor (when the customer deletes it).</summary>
    Task RemoveSavedAsync(string providerMethodId);

    /// <summary>Gives the money back for an earlier charge.</summary>
    Task<RefundResult> RefundAsync(string chargeId, decimal amount, string idempotencyKey);
}

public record PaymentMethodDetails(PaymentMethodType Type, string? Brand, string? Last4, int? ExpMonth, int? ExpYear);

public record ChargeRequest(
    decimal Amount,
    string Currency,
    string PaymentMethodToken,
    string IdempotencyKey,
    string Description,           // shown on the customer's statement and in the processor's dashboard
    string CustomerReference,     // our id for the customer at the processor
    string? BillingPostalCode);   // real processors use it for address verification (AVS)

/// <summary>Retryable = the processor had a problem, as opposed to the card being refused.</summary>
public record ChargeResult(bool Succeeded, string? ChargeId, string? DeclineCode, string? Message, bool Retryable = false);

public record RefundResult(bool Succeeded, string? RefundId);
