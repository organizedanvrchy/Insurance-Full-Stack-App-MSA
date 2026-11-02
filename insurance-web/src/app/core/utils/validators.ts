import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// ---- Password (keep in sync with Api/Validation/PasswordPolicy.cs) ----
export const PASSWORD_MIN = 8;
export const PASSWORD_MAX = 72;

export interface PasswordRule {
  id: 'length' | 'upper' | 'special';
  label: string;
  test: (password: string) => boolean;
}

export const PASSWORD_RULES: PasswordRule[] = [
  { id: 'length', label: `At least ${PASSWORD_MIN} characters`, test: (p) => p.length >= PASSWORD_MIN },
  { id: 'upper', label: 'One uppercase letter', test: (p) => /\p{Lu}/u.test(p) },
  // Special = anything that is not a letter, digit or whitespace
  { id: 'special', label: 'One special character, like ! @ # $ %', test: (p) => /[^\p{L}\p{Nd}\s]/u.test(p) },
];

export const strongPassword: ValidatorFn = (c: AbstractControl): ValidationErrors | null => {
  const value: string = c.value ?? '';
  if (!value) return null; // `required` reports an empty field
  const unmet = PASSWORD_RULES.filter((r) => !r.test(value)).map((r) => r.id);
  return unmet.length ? { strongPassword: { unmet } } : null;
};

// ---- Names, city and ZIP ----
export const NAME_PATTERN = /^[\p{L}\p{M}][\p{L}\p{M}'’.\- ]*$/u;
export const ZIP_PATTERN = /^\d{5}(-\d{4})?$/;

/** name@domain.tld. Angular's Validators.email alone accepts "a@b". Same rule as the API's EmailRules. */
export const EMAIL_PATTERN = /^[^@\s]+@[^@\s]+\.[^@\s]{2,}$/;

// ---- US phone (keep in sync with Api/Validation/PhoneNumbers.cs) ----

/** Returns "(203) 555-0141" for a valid US number in a common format, or null. */
export function normalizeUsPhone(input: string): string | null {
  const text = input.trim();
  if (!text || !/^[\d\s().+\-]+$/.test(text)) return null; // no letters or other symbols
  if (text.lastIndexOf('+') > 0) return null; // "+" only as a prefix

  let digits = text.replace(/\D/g, '');
  if (digits.length === 11 && digits.startsWith('1')) digits = digits.slice(1); // drop the country code
  if (digits.length !== 10) return null;
  if ('01'.includes(digits[0]) || '01'.includes(digits[3])) return null; // area code and exchange can't start with 0 or 1

  return `(${digits.slice(0, 3)}) ${digits.slice(3, 6)}-${digits.slice(6)}`;
}

/** Optional field: empty is fine, anything entered must be a valid US number. */
export const usPhone: ValidatorFn = (c: AbstractControl): ValidationErrors | null => {
  const value: string = (c.value ?? '').trim();
  if (!value) return null;
  return normalizeUsPhone(value) ? null : { usPhone: true };
};
