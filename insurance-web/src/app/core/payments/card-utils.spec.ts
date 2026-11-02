import { describe, expect, it } from 'vitest';
import { detectBrand, formatCardNumber, formatExpiry, parseExpiry, passesLuhn } from './card-utils';

describe('card utils', () => {
  it('detects the brand from the first digits', () => {
    expect(detectBrand('4242')).toBe('visa');
    expect(detectBrand('5555 5555')).toBe('mastercard');
    expect(detectBrand('2221 00')).toBe('mastercard');
    expect(detectBrand('3782')).toBe('amex');
    expect(detectBrand('6011')).toBe('discover');
    expect(detectBrand('9999')).toBeNull();
  });

  it('checks the Luhn checksum', () => {
    expect(passesLuhn('4242 4242 4242 4242')).toBe(true);
    expect(passesLuhn('4242 4242 4242 4241')).toBe(false);
    expect(passesLuhn('378282246310005')).toBe(true);
  });

  it('groups digits as the customer types', () => {
    expect(formatCardNumber('42424242424242429999')).toBe('4242 4242 4242 4242');
    expect(formatCardNumber('378282246310005')).toBe('3782 822463 10005');
    expect(formatExpiry('1228')).toBe('12/28');
  });

  it('accepts only real, unexpired months', () => {
    const now = new Date(2026, 10, 15);   // 15 November 2026
    expect(parseExpiry('11/26', now)).toEqual({ month: 11, year: 2026 });
    expect(parseExpiry('10/26', now)).toBeNull();
    expect(parseExpiry('13/30', now)).toBeNull();
    expect(parseExpiry('1230', now)).toBeNull();
  });
});
