// Mirrors Api/DTOs/Customers/CustomerDto.cs. createdAt is a UTC ISO string.
export interface Customer {
  userId: number;
  fullName: string;
  email: string;
  phone: string | null;
  isActive: boolean;
  emailVerified: boolean;
  createdAt: string;
  activePolicies: number;
  totalClaims: number;
  openClaims: number;
}
