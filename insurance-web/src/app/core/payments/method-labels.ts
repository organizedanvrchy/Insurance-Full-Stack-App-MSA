import { PaymentMethodType } from '../models/enums';
import { BRAND_LABELS } from './card-utils';

export const METHOD_LABELS: Record<PaymentMethodType, string> = {
  Card: 'Credit/debit card',
  PayPal: 'PayPal',
  ApplePay: 'Apple Pay',
  GooglePay: 'Google Pay',
  Venmo: 'Venmo',
};

/** How a payment method is shown in lists and receipts: "Visa •••• 4242", "PayPal". */
export function describeMethod(m: { methodType: PaymentMethodType; cardBrand: string | null; cardLast4: string | null }): string {
  if (m.methodType === 'Card') return `${BRAND_LABELS[m.cardBrand ?? ''] ?? 'Card'} •••• ${m.cardLast4}`;
  return METHOD_LABELS[m.methodType];
}
