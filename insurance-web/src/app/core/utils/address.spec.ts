import { describe, expect, it } from 'vitest';
import { parseAddress } from './address';

describe('parseAddress', () => {
  it('splits the sign-up address back into its parts', () => {
    expect(parseAddress('14 Elm Court, Apt 3B, Hartford, CT 06103')).toEqual({
      street: '14 Elm Court',
      unit: 'Apt 3B',
      city: 'Hartford',
      state: 'CT',
      postalCode: '06103',
    });
    expect(parseAddress('1 Main St, Stamford, CT 06902')?.unit).toBe('');
  });

  it('gives up on anything else', () => {
    expect(parseAddress(null)).toBeNull();
    expect(parseAddress('somewhere')).toBeNull();
  });
});
