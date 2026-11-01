using Insurance.Billing.Enums;
using Insurance.Billing.Gateway;
using Microsoft.Extensions.Options;
using Xunit;

namespace Insurance.Billing.Tests;

// The simulated processor behaves like a real one in test mode: the card decides the outcome.

public class MockPaymentGatewayTests
{
    private readonly MockPaymentGateway _gateway = new(Options.Create(new MockGatewayOptions { LatencyMs = 0 }));

    private Task<ChargeResult> Charge(string token, string key = "k-0001") =>
        _gateway.ChargeAsync(new ChargeRequest(50m, "USD", token, key, "test", "customer-1", "06902"));

    [Theory]
    [InlineData("tok_mock_card_visa_4242_1230")]
    [InlineData("tok_mock_card_mastercard_4444_1230")]
    [InlineData("tok_mock_card_amex_0005_1230")]
    [InlineData("tok_mock_applepay")]
    [InlineData("tok_mock_googlepay")]
    [InlineData("tok_mock_venmo")]
    public async Task Ordinary_methods_are_approved(string token)
    {
        var result = await Charge(token);

        Assert.True(result.Succeeded);
        Assert.StartsWith("ch_mock_", result.ChargeId);
    }

    [Theory]
    [InlineData("0002", "card_declined")]
    [InlineData("9995", "insufficient_funds")]
    [InlineData("0069", "expired_card")]
    [InlineData("0127", "incorrect_cvc")]
    [InlineData("0119", "processing_error")]
    public async Task Test_cards_are_declined_with_their_reason(string last4, string code)
    {
        var result = await Charge($"tok_mock_card_visa_{last4}_1230");

        Assert.False(result.Succeeded);
        Assert.Equal(code, result.DeclineCode);
    }

    [Fact]
    public async Task A_card_past_its_expiry_date_is_declined()
    {
        var result = await Charge("tok_mock_card_visa_4242_0120");   // January 2020

        Assert.Equal("expired_card", result.DeclineCode);
    }

    [Fact]
    public async Task The_same_idempotency_key_returns_the_first_charge()
    {
        var first = await Charge("tok_mock_card_visa_4242_1230", key: "k-0001");
        var again = await Charge("tok_mock_card_visa_4242_1230", key: "k-0001");

        Assert.Equal(first.ChargeId, again.ChargeId);
    }

    [Theory]
    [InlineData("4242424242424242")]                 // a raw card number is never accepted
    [InlineData("tok_mock_card_visa_42_1230")]        // bad last 4
    [InlineData("tok_mock_card_unknownbrand_4242_1230")]
    [InlineData("tok_mock_card_visa_4242_1330")]      // month 13
    [InlineData("")]
    public async Task Malformed_tokens_are_rejected(string token)
    {
        await Assert.ThrowsAsync<PaymentMethodInvalidException>(() => _gateway.DescribeAsync(token));
    }

    [Fact]
    public async Task Describing_a_card_token_gives_brand_last4_and_expiry()
    {
        var method = await _gateway.DescribeAsync("tok_mock_card_mastercard_4444_0731");

        Assert.Equal((PaymentMethodType.Card, "mastercard", "4444", 7, 2031),
            (method.Type, method.Brand, method.Last4, method.ExpMonth, method.ExpYear));
    }

    [Fact]
    public async Task A_saved_reference_describes_the_same_card_and_can_be_charged()
    {
        var saved = await _gateway.SaveForReuseAsync("tok_mock_card_visa_0002_1230", "customer-1");

        Assert.StartsWith("pm_mock_card_visa_0002_1230_", saved);
        Assert.Equal("card_declined", (await Charge(saved)).DeclineCode);   // still the same (declining) card
    }
}
