// Mirrors Identity's DTOs/Auth/EmailVerificationRequests.cs

export interface VerifyEmailResponse {
  email: string;
  emailChanged: boolean; // true when the link confirmed a NEW address
}

export interface ChangeEmailRequest {
  newEmail: string;
  currentPassword: string;
}
