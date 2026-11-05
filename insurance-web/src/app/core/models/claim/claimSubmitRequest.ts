export interface ClaimSubmitRequest {
  enrollmentId: number;
  incidentDate: string;
  description: string;
  claimAmount: number;
  /** How many files the form uploads right after: lets the confirmation email go out as soon as they're in. */
  documentCount?: number;
}
