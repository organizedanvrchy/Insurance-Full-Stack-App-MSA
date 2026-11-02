// Mirrors the Notifications service's DTOs (GET/PUT /api/notifications/preferences, GET /api/notifications/history).

export interface NotificationPreferences {
  paymentReminders: boolean;
}

export type NotificationKind =
  | 'VerifyEmail'
  | 'EmailChangeRequested'
  | 'EmailChanged'
  | 'PolicyActivated'
  | 'PolicyCancelled'
  | 'ClaimSubmitted'
  | 'ClaimStatusChanged'
  | 'PaymentReceipt'
  | 'PaymentReminder';

export interface SentEmail {
  emailId: number;
  kind: NotificationKind;
  subject: string | null;
  toEmail: string | null;
  sentAt: string | null; // UTC ISO string
}
