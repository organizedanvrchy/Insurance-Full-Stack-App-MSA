// Mirrors Identity's DTOs/Auth/EmailVerificationRequests.cs and AccountRequests.cs

export interface VerifyEmailResponse {
  email: string; // the address that is now verified
}

export interface ChangeEmailRequest {
  newEmail: string;
}

/** After an admin reset: signing in with the one-time password was the proof, so no current password. */
export interface ChooseNewPasswordRequest {
  newPassword: string;
  confirmNewPassword: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmNewPassword: string;
}
