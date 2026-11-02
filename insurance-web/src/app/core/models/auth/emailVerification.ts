// Mirrors Identity's DTOs/Auth/EmailVerificationRequests.cs and AccountRequests.cs

export interface VerifyEmailResponse {
  email: string; // the address that is now verified
}

export interface ChangeEmailRequest {
  newEmail: string;
  currentPassword: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmNewPassword: string;
}
