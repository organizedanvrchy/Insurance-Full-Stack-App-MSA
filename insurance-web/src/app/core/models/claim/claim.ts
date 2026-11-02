import { ClaimStatus, PolicyType } from '../enums';

// Mirrors Api/DTOs/Claims/ClaimDto.cs. Dates arrive as strings; timestamps are UTC ISO strings.
export interface Claim {
  claimId: number;
  claimNumber: string;
  enrollmentId: number;
  policyName: string;
  policyType: PolicyType;
  customerName: string;
  customerEmail: string;
  incidentDate: string;
  description: string;
  claimAmount: number;
  status: ClaimStatus;
  adminNotes: string | null;
  submittedAt: string;
  reviewedAt: string | null;
  reviewedBy: string | null;
  allowedNextStatuses: ClaimStatus[];   // only populated for admins
  canAddDocuments: boolean;             // only true for the customer
  canSendToReviewer: boolean;           // only true for the customer
}
