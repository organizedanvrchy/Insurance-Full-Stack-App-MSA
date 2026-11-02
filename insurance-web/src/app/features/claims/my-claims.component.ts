import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ClaimService } from '../../core/services/claim.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { claimBadgeClass, statusLabel } from '../../core/utils/labels';
import { Claim } from '../../core/models/claim/claim';
import { ClaimDocumentsComponent } from './claim-documents.component';

@Component({
  selector: 'app-my-claims',
  imports: [RouterLink, CurrencyPipe, DatePipe, ClaimDocumentsComponent],
  templateUrl: "my-claims.html",
})
export class MyClaimsComponent {
  private api = inject(ClaimService);
  private route = inject(ActivatedRoute);

  readonly label = statusLabel;
  readonly badgeClass = claimBadgeClass;

  claims = signal<Claim[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  busy = signal(false);
  openDocumentsFor = signal<number | null>(null);

  // Set when the claim was filed but one of its documents wasn't uploaded (see ClaimFormComponent).
  readonly documentsFailed = Number(this.route.snapshot.queryParamMap.get('documentsFailed')) || null;

  constructor() {
    this.openDocumentsFor.set(this.documentsFailed);

    this.api.getMine().subscribe({
      next: (list) => {
        this.claims.set(list);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(getErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  toggleDocuments(claimId: number): void {
    this.openDocumentsFor.update((open) => (open === claimId ? null : claimId));
  }

  sendToReviewer(claim: Claim): void {
    this.busy.set(true);
    this.error.set(null);

    this.api.sendToReviewer(claim.claimId).subscribe({
      next: (updated) => {
        this.claims.update((list) => list.map((c) => (c.claimId === updated.claimId ? updated : c)));
        this.busy.set(false);
      },
      error: (err) => {
        this.error.set(getErrorMessage(err));
        this.busy.set(false);
      },
    });
  }
}
