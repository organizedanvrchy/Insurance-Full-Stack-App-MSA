// Mirrors Api/DTOs/Customers/CustomerDto.cs. createdAt is a UTC ISO string.
export interface Customer {
  userId: number;
  fullName: string;
  email: string;
  phone: string | null;
  address: string | null;          // one line: "48 Maple Avenue, Apt 3B, Stamford, CT 06902"
  isActive: boolean;
  passwordResetPending: boolean;   // an admin reset the password; the customer hasn't chosen a new one yet
  emailVerified: boolean;
  createdAt: string;
  activePolicies: number;
  totalClaims: number;
  openClaims: number;
}

/** What an admin can change on a customer (PUT /api/admin/customers/{id}); the same fields as sign-up. */
export interface CustomerDetailsUpdate {
  firstName: string;
  lastName: string;
  email: string;
  phone?: string;
  street: string;
  unit?: string;
  city: string;
  state: string;
  postalCode: string;
}
