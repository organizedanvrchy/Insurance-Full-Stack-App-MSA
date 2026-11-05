import { UserRole } from "../enums";

export interface User {
  userId: number;
  fullName: string;
  email: string;
  role: UserRole;
  emailVerified: boolean;          // enrolling and filing claims need a verified email
  mustChangePassword?: boolean;    // an admin reset the password: a new one must be chosen first
}
