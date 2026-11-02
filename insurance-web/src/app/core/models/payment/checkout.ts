import { CheckoutOutcome, PaymentMethodType, TransactionStatus } from '../enums';
import { Payment } from './payment';

// Mirrors Billing's DTOs/CheckoutRequest.cs. Exactly one of paymentMethodToken / savedPaymentMethodId.
// There is no field for a card number: card details are turned into a token in the browser and never sent.
export interface CheckoutRequest {
  paymentMethodToken?: string;
  savedPaymentMethodId?: number;
  idempotencyKey: string;
  saveMethod: boolean;
  billingName?: string;
  billingPostalCode?: string;
}

// Mirrors DTOs/TransactionDto.cs: one line in the payment log, or a receipt.
export interface Transaction {
  transactionId: number;
  reference: string;          // TXN-000042
  paymentId: number;
  enrollmentId: number;
  amount: number;
  currency: string;
  methodType: PaymentMethodType;
  cardBrand: string | null;
  cardLast4: string | null;
  provider: string;
  providerChargeId: string | null;
  status: TransactionStatus;
  declineCode: string | null;
  message: string | null;
  billingName: string | null;
  createdAt: string;
  completedAt: string | null;
  // admin payment log only
  userId: number | null;
  customerName: string | null;
  customerEmail: string | null;
}

// Mirrors DTOs/CheckoutResultDto.cs. A decline is a normal answer (HTTP 200), not an error.
export interface CheckoutResult {
  outcome: CheckoutOutcome;
  message: string;
  transaction: Transaction;
  payment: Payment;
}

// Mirrors DTOs/SavedPaymentMethodDto.cs.
export interface SavedPaymentMethod {
  paymentMethodId: number;
  methodType: PaymentMethodType;
  cardBrand: string | null;
  cardLast4: string | null;
  expMonth: number | null;
  expYear: number | null;
  holderName: string | null;
  createdAt: string;
}
