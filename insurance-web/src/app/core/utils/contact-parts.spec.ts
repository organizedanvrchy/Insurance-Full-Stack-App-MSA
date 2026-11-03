import { splitAddress, splitName } from './contact-parts';

describe('contact-parts', () => {
  it('splits a full name into first and last', () => {
    expect(splitName('Anne-Marie  van der Berg')).toEqual({ firstName: 'Anne-Marie', lastName: 'van der Berg' });
    expect(splitName('Cher')).toEqual({ firstName: 'Cher', lastName: '' });
  });

  it('splits an address with and without a unit', () => {
    expect(splitAddress('48 Maple Avenue, Apt 3B, Stamford, CT 06902')).toEqual({
      street: '48 Maple Avenue', unit: 'Apt 3B', city: 'Stamford', state: 'CT', postalCode: '06902',
    });
    expect(splitAddress('905 Orchard Street, New Haven, CT 06511-1234')).toEqual({
      street: '905 Orchard Street', unit: '', city: 'New Haven', state: 'CT', postalCode: '06511-1234',
    });
  });

  it('leaves an address it cannot read in the street field', () => {
    expect(splitAddress('somewhere odd')).toEqual({ street: 'somewhere odd', unit: '', city: '', state: '', postalCode: '' });
    expect(splitAddress(null).street).toBe('');
  });
});
