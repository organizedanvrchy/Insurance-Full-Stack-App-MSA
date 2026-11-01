namespace Insurance.Billing.Enums;

// The life of one payment attempt. Keep in sync with CK_PaymentTransactions_Status.
public enum TransactionStatus
{
    Processing,   // sent to the gateway, no answer yet
    Succeeded,    // money taken and the installment marked paid
    Declined,     // the card or wallet refused (the customer can try another method)
    Failed,       // the gateway had a problem (the customer can try again)
    Refunded      // money was taken but the installment couldn't be marked paid, so it was given back
}
