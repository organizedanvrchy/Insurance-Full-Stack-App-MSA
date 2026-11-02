import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { CLAIM_STATUSES, ClaimStatus } from '../../core/models/enums';
import { ClaimService } from '../../core/services/claim.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { CLAIM_ACTION_LABELS, claimBadgeClass, statusLabel } from '../../core/utils/labels';
import { Claim } from '../../core/models/claim/claim';
import { ClaimDocumentsComponent } from './claim-documents.component';

@Component({
  selector: 'app-admin-claims',
  imports: [CurrencyPipe, DatePipe, ClaimDocumentsComponent],
  templateUrl: "admin-claims.html",
  styleUrl: "admin-claims.component.css",
})
export class AdminClaimsComponent {
  private api = inject(ClaimService);

  readonly statuses = CLAIM_STATUSES;
  readonly actionLabels = CLAIM_ACTION_LABELS;
  readonly label = statusLabel;
  readonly badgeClass = claimBadgeClass;

  claims = signal<Claim[]>([]);
  selected = signal<Claim | null>(null);
  filter = signal<ClaimStatus | ''>('');
  loading = signal(true);
  busy = signal(false);
  error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  setFilter(value: string): void {
    this.filter.set(value as ClaimStatus | '');
    this.load();
  }

  act(c: Claim, status: ClaimStatus, notes: string): void {
    const trimmed = notes.trim();
    if (status === 'Rejected' && !trimmed) {
      this.error.set('Please enter a reason before rejecting this claim.');
      return;
    }
    if (status === 'NeedsMoreInfo' && !trimmed) {
      this.error.set('Please tell the customer what information you need.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    this.api.review(c.claimId, { status, adminNotes: trimmed || undefined }).subscribe({
      next: (updated) => {
        this.selected.set(updated);   // show the new state (and the next allowed buttons) immediately
        this.busy.set(false);
        this.load();
      },
      error: (err) => {
        this.error.set(getErrorMessage(err));   // e.g. 409 "changed by someone else"
        this.busy.set(false);
        this.load();                            // re-sync so we're not looking at stale data
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.api.getAll(this.filter() || undefined).subscribe({
      next: (list) => {
        this.claims.set(list);
        this.loading.set(false);

        // Refresh the open panel if its claim is still in the list (it may have disappeared because of the filter).
        const open = this.selected();
        const fresh = open ? list.find((x) => x.claimId === open.claimId) : undefined;
        if (fresh) this.selected.set(fresh);
      },
      error: (err) => {
        this.error.set(getErrorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
