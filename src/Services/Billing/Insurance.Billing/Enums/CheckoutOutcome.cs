namespace Insurance.Billing.Enums;

// What the checkout page should show. A decline is a normal, recorded outcome (HTTP 200), not an error.
public enum CheckoutOutcome
{
    Succeeded,
    Declined
}
