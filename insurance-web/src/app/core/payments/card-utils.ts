// Card number helpers for the checkout form. Pure functions, no Angular.

export type CardBrand = 'visa' | 'mastercard' | 'amex' | 'discover';

export const BRAND_LABELS: Record<string, string> = {
  visa: 'Visa',
  mastercard: 'Mastercard',
  amex: 'American Express',
  discover: 'Discover',
};

export function digitsOnly(value: string): string {
  return value.replace(/\D/g, '');
}

/** From the first digits, as the customer types. null = not a brand we accept (or not enough digits yet). */
export function detectBrand(number: string): CardBrand | null {
  const n = digitsOnly(number);
  if (/^4/.test(n)) return 'visa';
  if (/^(5[1-5]|2(2[2-9]|[3-6]\d|7[01]|720))/.test(n)) return 'mastercard';
  if (/^3[47]/.test(n)) return 'amex';
  if (/^6(011|5|4[4-9])/.test(n)) return 'discover';
  return null;
}

export function cardLength(brand: CardBrand | null): number {
  return brand === 'amex' ? 15 : 16;
}

export function cvcLength(brand: CardBrand | null): number {
  return brand === 'amex' ? 4 : 3;
}

/** "4242424242424242" -> "4242 4242 4242 4242"; American Express groups as 4-6-5. */
export function formatCardNumber(value: string): string {
  const brand = detectBrand(value);
  const n = digitsOnly(value).slice(0, cardLength(brand));
  const groups = brand === 'amex' ? [4, 6, 5] : [4, 4, 4, 4];
  const parts: string[] = [];
  let i = 0;
  for (const size of groups) {
    if (i >= n.length) break;
    parts.push(n.slice(i, i + size));
    i += size;
  }
  return parts.join(' ');
}

/** "1228" or "12/28" -> "12/28" as the customer types. */
export function formatExpiry(value: string): string {
  const n = digitsOnly(value).slice(0, 4);
  return n.length > 2 ? `${n.slice(0, 2)}/${n.slice(2)}` : n;
}

/** The Luhn checksum every real card number passes; catches most typos before anything is sent. */
export function passesLuhn(number: string): boolean {
  const n = digitsOnly(number);
  if (n.length < 12) return false;
  let sum = 0;
  for (let i = 0; i < n.length; i++) {
    let d = Number(n[n.length - 1 - i]);
    if (i % 2 === 1) {
      d *= 2;
      if (d > 9) d -= 9;
    }
    sum += d;
  }
  return sum % 10 === 0;
}

/** "MM/YY" -> { month, year } if it's a real month that hasn't ended yet. */
export function parseExpiry(value: string, now = new Date()): { month: number; year: number } | null {
  const match = /^(\d{2})\/(\d{2})$/.exec(value);
  if (!match) return null;
  const month = Number(match[1]);
  const year = 2000 + Number(match[2]);
  if (month < 1 || month > 12) return null;
  const endOfMonth = new Date(year, month, 1);   // first day of the following month
  return endOfMonth > now ? { month, year } : null;
}
