import { NotificationKind } from '../models/notification/notification';
import { ClaimStatus } from '../models/enums';

/** "UnderReview" -> "Under review" */
export function statusLabel(status: string): string {
  const spaced = status.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0) + spaced.slice(1).toLowerCase();
}

/** Extra CSS class for the badge colour (empty string = the default green). */
export function claimBadgeClass(status: ClaimStatus): string {
  switch (status) {
    case 'Submitted':
      return 'info';
    case 'UnderReview':
      return 'amber';
    case 'Rejected':
      return 'warn';
    default:
      return '';
  }
}

/** Button text for each status an admin can move a claim TO. */
export const CLAIM_ACTION_LABELS: Record<ClaimStatus, string> = {
  Submitted: 'Submitted',
  UnderReview: 'Start review',
  Approved: 'Approve',
  Rejected: 'Reject',
  Paid: 'Mark as paid',
};

/** What each kind of email is about, for the "Recent emails" list on the profile page. */
export function notificationKindLabel(kind: NotificationKind): string {
  switch (kind) {
    case 'VerifyEmail':
      return 'Verification';
    case 'EmailChangeRequested':
    case 'EmailChanged':
      return 'Account';
    case 'PolicyActivated':
    case 'PolicyCancelled':
      return 'Policy';
    case 'ClaimSubmitted':
    case 'ClaimStatusChanged':
      return 'Claim';
    case 'PaymentReceipt':
      return 'Receipt';
    case 'PaymentReminder':
      return 'Reminder';
  }
}
