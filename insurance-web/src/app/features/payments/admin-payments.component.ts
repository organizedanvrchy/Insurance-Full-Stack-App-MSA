import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TRANSACTION_STATUSES, TransactionStatus } from '../../core/models/enums';
import { Transaction } from '../../core/models/payment/checkout';
import { describeMethod } from '../../core/payments/method-labels';
import { PaymentService } from '../../core/services/payment.service';
import { getErrorMessage } from '../../core/utils/http-error';

/** Admin: every payment attempt from every customer, newest first (the payment log). */
@Component({
  selector: 'app-admin-payments',
  imports: [RouterLink, CurrencyPipe, DatePipe],
  templateUrl: 'admin-payments.html',
  styles: '.nowrap { white-space: nowrap; }',
})
export class AdminPaymentsComponent {
  private api = inject(PaymentService);

  readonly statuses = TRANSACTION_STATUSES;

  transactions = signal<Transaction[]>([]);
  filter = signal<TransactionStatus | ''>('');
  loading = signal(true);
  error = signal<string | null>(null);

  // Totals for the cards at the top (over what is listed).
  collected = computed(() =>
    this.transactions().filter((t) => t.status === 'Succeeded').reduce((sum, t) => sum + t.amount, 0),
  );
  succeeded = computed(() => this.transactions().filter((t) => t.status === 'Succeeded').length);
  problems = computed(() => this.transactions().filter((t) => t.status === 'Declined' || t.status === 'Failed').length);
  refunded = computed(() => this.transactions().filter((t) => t.status === 'Refunded').length);

  constructor() {
    this.load();
  }

  setFilter(value: string): void {
    this.filter.set(value as TransactionStatus | '');
    this.load();
  }

  readonly methodText = describeMethod;

  private load(): void {
    this.loading.set(true);
    this.api.getTransactions({ status: this.filter() || undefined }).subscribe({
      next: (list) => {
        this.transactions.set(list);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(getErrorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
