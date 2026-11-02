// Early feedback only. The Claims service makes the real decision, and checks a file's actual contents.
export const MAX_DOCUMENTS_PER_CLAIM = 5;
export const MAX_DOCUMENT_BYTES = 10 * 1024 * 1024;
export const CLAIM_DOCUMENT_ACCEPT = '.pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png';

const ALLOWED_EXTENSIONS = /\.(pdf|jpe?g|png)$/i;

/** Why this file can't be uploaded, or null if it looks fine. */
export function claimDocumentProblem(file: File): string | null {
  if (!ALLOWED_EXTENSIONS.test(file.name)) return `${file.name} isn't a PDF, JPG or PNG file.`;
  if (file.size === 0) return `${file.name} is empty.`;
  if (file.size > MAX_DOCUMENT_BYTES) return `${file.name} is larger than 10 MB.`;
  return null;
}

/** 2048 -> "2 KB" */
export function fileSizeLabel(bytes: number): string {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
