/**
 * The API stores a customer's name and address as single lines ("Jane Doe", "48 Maple Avenue, Apt 3B, Stamford, CT
 * 06902"), built from the sign-up form's separate fields. The admin edit form shows those fields again, so these
 * split the lines back up. An address that doesn't fit the pattern goes into the street field for the admin to fix.
 */

export function splitName(fullName: string): { firstName: string; lastName: string } {
  const trimmed = fullName.trim();
  const space = trimmed.indexOf(' ');
  return space < 0
    ? { firstName: trimmed, lastName: '' }
    : { firstName: trimmed.slice(0, space), lastName: trimmed.slice(space + 1).trim() };
}

export interface AddressParts {
  street: string;
  unit: string;
  city: string;
  state: string;
  postalCode: string;
}

// street[, unit], city, ST 12345[-6789]
const ADDRESS = /^(.+?),\s*(?:(.+?),\s*)?([^,]+),\s*([A-Za-z]{2})\s+(\d{5}(?:-\d{4})?)$/;

export function splitAddress(address: string | null): AddressParts {
  const m = ADDRESS.exec((address ?? '').trim());
  if (!m) return { street: (address ?? '').trim(), unit: '', city: '', state: '', postalCode: '' };
  return { street: m[1].trim(), unit: (m[2] ?? '').trim(), city: m[3].trim(), state: m[4].toUpperCase(), postalCode: m[5] };
}
