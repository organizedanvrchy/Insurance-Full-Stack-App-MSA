/** A sign-up address split back into its parts. */
export interface AddressParts {
  street: string;
  unit: string;
  city: string;
  state: string;
  postalCode: string;
}

/**
 * Reads the address stored at sign-up, "48 Maple Avenue, Apt 3B, Stamford, CT 06902"
 * (Identity's ContactFormatter.Address; the unit is optional), so forms can be pre-filled.
 * Returns null if it doesn't have that shape.
 */
export function parseAddress(address: string | null | undefined): AddressParts | null {
  if (!address) return null;
  const parts = address.split(',').map((p) => p.trim());
  if (parts.length < 3) return null;

  const stateZip = /^([A-Z]{2})\s+(\d{5}(?:-\d{4})?)$/.exec(parts[parts.length - 1]);
  if (!stateZip) return null;

  return {
    street: parts[0],
    unit: parts.length > 3 ? parts.slice(1, -2).join(', ') : '',
    city: parts[parts.length - 2],
    state: stateZip[1],
    postalCode: stateZip[2],
  };
}
