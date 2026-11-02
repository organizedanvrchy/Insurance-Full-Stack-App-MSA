import { Injectable, inject } from '@angular/core';
import { PaymentMethodType } from '../models/enums';
import { MockPaymentProvider } from './mock-payment-provider';

/** What the customer typed. Exists only in the browser: it is never sent to our API. */
export interface CardDetails {
  number: string;
  expiry: string;        // MM/YY
  cvc: string;
  name: string;
  postalCode: string;
}

/** What the provider gives back: a token our API can charge, plus what is safe to show. */
export interface TokenizedMethod {
  token: string;
  methodType: PaymentMethodType;
  brand?: string;
  last4?: string;
}

/**
 * The browser side of a payment processor. Card details go to the processor (here: the simulation),
 * which answers with a one-time token; the token is all our API ever receives.
 *
 * Switching to a real processor means writing one class with these two methods
 * (for example with Stripe.js and its Payment Element) and changing the factory below.
 * The checkout page only knows this class.
 */
@Injectable({ providedIn: 'root', useFactory: () => inject(MockPaymentProvider) })
export abstract class PaymentProvider {
  /** Shown on the checkout page, e.g. "Simulated" or "Stripe". */
  abstract readonly displayName: string;

  /** Validates the card and exchanges it for a token. Rejects with a message the customer can act on. */
  abstract tokenizeCard(card: CardDetails): Promise<TokenizedMethod>;

  /** Opens the wallet's approval step (PayPal login, Apple Pay sheet...) and returns a token once approved. */
  abstract approveWallet(type: Exclude<PaymentMethodType, 'Card'>): Promise<TokenizedMethod>;
}
