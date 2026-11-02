// Mirrors ClaimDocumentDto.cs. uploadedAt is a UTC ISO string.
export interface ClaimDocument {
  claimDocumentId: number;
  claimId: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedAt: string;
}
