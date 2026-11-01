namespace Insurance.Billing.Enums;

// Keep in sync with CK_PaymentTransactions_Method and the Angular PAYMENT_METHOD_TYPES.
public enum PaymentMethodType
{
    Card,        // credit or debit
    PayPal,
    ApplePay,
    GooglePay,
    Venmo
}
