import { UserRole } from "../enums";

export interface User {
  userId: number;
  fullName: string;
  email: string;
  role: UserRole;
  emailVerified: boolean;          // enrolling and filing claims need a verified email
}
