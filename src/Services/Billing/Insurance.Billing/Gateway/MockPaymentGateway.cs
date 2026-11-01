using System.Collections.Concurrent;
using Insurance.Billing.Enums;
using Microsoft.Extensions.Options;

namespace Insurance.Billing.Gateway;

/// <summary>"Payments:Mock" section of the configuration.</summary>
public class MockGatewayOptions
{
    // A real processor takes a moment to answer; the checkout page shows "Processing..." meanwhile.
    public int LatencyMs { get; set; } = 700;
}

/// <summary>
/// A simulated payment processor that behaves like a real one: tokens instead of card numbers, declines,
/// idempotent charges, saved methods and refunds. No money moves and nothing leaves this process.
///
/// Tokens are made by the Angular app's mock tokenizer (core/payments/mock-payment-provider.ts):
///   tok_mock_card_{brand}_{last4}_{MMYY}   a card, e.g. tok_mock_card_visa_4242_1228
///   tok_mock_{paypal|applepay|googlepay|venmo}
///   pm_mock_...                              a saved (reusable) method made by SaveForReuseAsync
///
/// Like a processor's test mode, the card's last 4 digits choose the outcome (see TestCards).
/// </summary>
public class MockPaymentGateway : IPaymentGateway
{
    // Same idea as Stripe's test cards: these last 4 digits are refused with a specific reason.
    public static readonly IReadOnlyDictionary<string, (string Code, string Message, bool Retryable)> TestCards =
        new Dictionary<string, (string, string, bool)>
        {
            ["0002"] = ("card_declined", "Your card was declined.", false),
            ["9995"] = ("insufficient_funds", "Your card has insufficient funds.", false),
            ["0069"] = ("expired_card", "Your card has expired.", false),
            ["0127"] = ("incorrect_cvc", "Your card's security code is incorrect.", false),
            ["0119"] = ("processing_error", "An error occurred while processing your card. Please try again.", true),
        };

    private static readonly Dictionary<string, PaymentMethodType> Wallets = new()
    {
        ["paypal"] = PaymentMethodType.PayPal,
        ["applepay"] = PaymentMethodType.ApplePay,
        ["googlepay"] = PaymentMethodType.GooglePay,
        ["venmo"] = PaymentMethodType.Venmo,
    };

    private static readonly HashSet<string> Brands = new() { "visa", "mastercard", "amex", "discover" };

    // A processor remembers idempotency keys, so a retried request gets the first answer.
    private readonly ConcurrentDictionary<string, ChargeResult> _charges = new();
    private readonly ConcurrentDictionary<string, RefundResult> _refunds = new();
    private readonly MockGatewayOptions _options;

    public MockPaymentGateway(IOptions<MockGatewayOptions> options) => _options = options.Value;

    public string Name => "Mock";

    public Task<PaymentMethodDetails> DescribeAsync(string paymentMethodToken) =>
        Task.FromResult(Parse(paymentMethodToken));

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request)
    {
        if (_charges.TryGetValue(request.IdempotencyKey, out var earlier))
            return earlier;

        var method = Parse(request.PaymentMethodToken);
        await Task.Delay(_options.LatencyMs);

        var result = Decide(method);
        return _charges.GetOrAdd(request.IdempotencyKey, result);
    }

    // tok_mock_card_visa_4242_1228 -> pm_mock_card_visa_4242_1228_<unique id>: still describes the same card.
    public Task<string> SaveForReuseAsync(string paymentMethodToken, string customerReference)
    {
        Parse(paymentMethodToken);   // throws if it isn't a usable token
        if (!paymentMethodToken.StartsWith("tok_mock_"))
            throw new PaymentMethodInvalidException("Only a newly entered payment method can be saved.");

        var body = paymentMethodToken["tok_mock_".Length..];
        return Task.FromResult($"pm_mock_{body}_{Guid.NewGuid().ToString("N")[..12]}");
    }

    public Task RemoveSavedAsync(string providerMethodId) => Task.CompletedTask;

    public Task<RefundResult> RefundAsync(string chargeId, decimal amount, string idempotencyKey) =>
        Task.FromResult(_refunds.GetOrAdd(idempotencyKey, _ => new RefundResult(true, "re_mock_" + Guid.NewGuid().ToString("N")[..24])));

    private static ChargeResult Decide(PaymentMethodDetails method)
    {
        if (method.Type != PaymentMethodType.Card)
            return Approved();   // wallets are approved by the customer in the wallet's own window

        var today = DateTime.UtcNow;
        if (method.ExpYear < today.Year || (method.ExpYear == today.Year && method.ExpMonth < today.Month))
            return new ChargeResult(false, null, "expired_card", "Your card has expired.");

        if (method.Last4 is not null && TestCards.TryGetValue(method.Last4, out var decline))
            return new ChargeResult(false, decline.Retryable ? null : NewChargeId(), decline.Code, decline.Message, decline.Retryable);

        return Approved();
    }

    private static ChargeResult Approved() => new(true, NewChargeId(), null, "Payment approved.");

    private static string NewChargeId() => "ch_mock_" + Guid.NewGuid().ToString("N")[..24];

    // tok_mock_card_visa_4242_1228 / tok_mock_paypal / pm_mock_card_visa_4242_1228_<id> / pm_mock_paypal_<id>
    private static PaymentMethodDetails Parse(string? token)
    {
        string body;
        if (token?.StartsWith("tok_mock_") == true) body = token["tok_mock_".Length..];
        else if (token?.StartsWith("pm_mock_") == true) body = token["pm_mock_".Length..];
        else throw new PaymentMethodInvalidException("That payment method could not be read. Please enter it again.");

        var parts = body.Split('_');
        if (Wallets.TryGetValue(parts[0], out var wallet))
            return new PaymentMethodDetails(wallet, null, null, null, null);

        if (parts is ["card", var brand, var last4, var expiry, ..]
            && Brands.Contains(brand)
            && last4.Length == 4 && last4.All(char.IsAsciiDigit)
            && expiry.Length == 4 && int.TryParse(expiry[..2], out var month) && int.TryParse(expiry[2..], out var year)
            && month is >= 1 and <= 12)
        {
            return new PaymentMethodDetails(PaymentMethodType.Card, brand, last4, month, 2000 + year);
        }

        throw new PaymentMethodInvalidException("That payment method could not be read. Please enter it again.");
    }
}
