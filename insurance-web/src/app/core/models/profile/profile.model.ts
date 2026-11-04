import { UserRole } from '../enums';

// Mirrors Api/DTOs/Auth/UserDto.cs, as returned by GET /api/auth/me
export interface UserProfile {
  userId: number;
  fullName: string;
  email: string;
  role: UserRole;
  phone: string | null;
  address: string | null;
  createdAt: string;   // UTC ISO string
  emailVerified: boolean;
  mustChangePassword?: boolean;
}
