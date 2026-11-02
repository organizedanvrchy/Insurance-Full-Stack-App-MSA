import { Injectable } from '@angular/core';
import { PaymentMethodType } from '../models/enums';
import { BRAND_LABELS, cardLength, cvcLength, detectBrand, digitsOnly, parseExpiry, passesLuhn } from './card-utils';
import type { CardDetails, PaymentProvider, TokenizedMethod } from './payment-provider';

/** Test cards, like a real processor's test mode. The last 4 digits decide what the (simulated) bank answers. */
export const TEST_CARDS = [
  { number: '4242 4242 4242 4242', label: 'Visa: approved' },
  { number: '5555 5555 5555 4444', label: 'Mastercard: approved' },
  { number: '3782 822463 10005', label: 'American Express: approved' },
  { number: '6011 1111 1111 1117', label: 'Discover: approved' },
  { number: '4000 0000 0000 0002', label: 'Declined' },
  { number: '4000 0000 0000 9995', label: 'Declined: insufficient funds' },
  { number: '4000 0000 0000 0069', label: 'Declined: expired card' },
  { number: '4000 0000 0000 0127', label: 'Declined: incorrect security code' },
  { number: '4000 0000 0000 0119', label: 'Processing error (try again)' },
];

const pause = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * The simulated processor's browser library. It validates the card the way a real one does,
 * then makes a token that describes the card without its number: tok_mock_card_visa_4242_1228.
 * The Billing service's MockPaymentGateway reads that token on the other side.
 */
@Injectable({ providedIn: 'root' })
export class MockPaymentProvider implements PaymentProvider {
  readonly displayName = 'Simulated';

  async tokenizeCard(card: CardDetails): Promise<TokenizedMethod> {
    const number = digitsOnly(card.number);
    const brand = detectBrand(number);

    if (!brand) throw new Error('We accept Visa, Mastercard, American Express and Discover.');
    if (number.length !== cardLength(brand) || !passesLuhn(number)) throw new Error('Your card number is not valid.');

    const expiry = parseExpiry(card.expiry);
    if (!expiry) throw new Error('Your card\'s expiry date is not valid or has passed.');

    if (digitsOnly(card.cvc).length !== cvcLength(brand))
      throw new Error(`Your card's security code is ${cvcLength(brand)} digits.`);

    await pause(300);   // a real library talks to the processor here

    const last4 = number.slice(-4);
    const mmyy = `${String(expiry.month).padStart(2, '0')}${String(expiry.year % 100).padStart(2, '0')}`;
    return { token: `tok_mock_card_${brand}_${last4}_${mmyy}`, methodType: 'Card', brand: BRAND_LABELS[brand], last4 };
  }

  async approveWallet(type: Exclude<PaymentMethodType, 'Card'>): Promise<TokenizedMethod> {
    await pause(500);   // the wallet's own approval window
    return { token: `tok_mock_${type.toLowerCase()}`, methodType: type };
  }
}
