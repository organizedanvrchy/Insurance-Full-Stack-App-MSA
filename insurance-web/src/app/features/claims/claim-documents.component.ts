import { DatePipe } from '@angular/common';
import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { concatMap, from, toArray } from 'rxjs';
import { ClaimDocument } from '../../core/models/claim/claimDocument';
import { ClaimService } from '../../core/services/claim.service';
import {
  CLAIM_DOCUMENT_ACCEPT,
  claimDocumentProblem,
  fileSizeLabel,
} from '../../core/utils/claim-documents';
import { getErrorMessage } from '../../core/utils/http-error';

// The documents on one claim: used by the customer's My claims page and the admin review panel.
@Component({
  selector: 'app-claim-documents',
  imports: [DatePipe],
  templateUrl: 'claim-documents.html',
  styles: `
    .add-document {
      margin-top: 0.75rem;
    }
  `,
})
export class ClaimDocumentsComponent {
  private api = inject(ClaimService);

  claimId = input.required<number>();
  canAdd = input(false);

  documents = signal<ClaimDocument[]>([]);
  loading = signal(true);
  busy = signal(false);
  error = signal<string | null>(null);

  readonly accept = CLAIM_DOCUMENT_ACCEPT;
  readonly sizeLabel = fileSizeLabel;

  constructor() {
    // Reloads when the admin opens a different claim in the same panel.
    effect(() => {
      const claimId = this.claimId();
      untracked(() => this.load(claimId));
    });
  }

  upload(picker: HTMLInputElement): void {
    const files = Array.from(picker.files ?? []);
    picker.value = '';     // so choosing the same file again still fires the change event
    if (files.length === 0) return;

    const problem = files.map(claimDocumentProblem).find((p) => p !== null);
    if (problem) {
      this.error.set(problem);
      return;
    }

    const claimId = this.claimId();
    this.busy.set(true);
    this.error.set(null);

    from(files)
      .pipe(
        concatMap((file) => this.api.uploadDocument(claimId, file)),
        toArray(),
      )
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.load(claimId);
        },
        error: (err) => {
          this.error.set(getErrorMessage(err));   // e.g. 409 too many documents, 400 not a real PDF
          this.busy.set(false);
          this.load(claimId);                     // earlier files in the batch may have gone through
        },
      });
  }

  download(file: ClaimDocument): void {
    this.error.set(null);
    this.api.downloadDocument(file.claimId, file.claimDocumentId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = file.fileName;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.error.set(getErrorMessage(err)),
    });
  }

  private load(claimId: number): void {
    this.loading.set(true);
    this.api.getDocuments(claimId).subscribe({
      next: (list) => {
        this.documents.set(list);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(getErrorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
