import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { EnrollmentService } from '../../core/services/enrollment.service';
import { PaymentService } from '../../core/services/payment.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { Enrollment } from '../../core/models/enrollment/enrollment';
import { Transaction } from '../../core/models/payment/checkout';
import { Payment } from '../../core/models/payment/payment';
import { describeMethod } from '../../core/payments/method-labels';

@Component({
  selector: 'app-payment-schedule',
  imports: [RouterLink, CurrencyPipe, DatePipe],
  templateUrl: "payment-schedule.html",
  styleUrl: "payment-schedule.component.css",
})
export class PaymentScheduleComponent {
  private route = inject(ActivatedRoute);
  private enrollmentsApi = inject(EnrollmentService);
  private paymentsApi = inject(PaymentService);

  private readonly enrollmentId = Number(this.route.snapshot.paramMap.get('id'));

  enrollment = signal<Enrollment | null>(null);
  payments = signal<Payment[]>([]);
  transactions = signal<Transaction[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  // Derived values: recompute automatically whenever `payments` changes.
  nextPayable = computed(() => this.payments().find((p) => !p.paidDate) ?? null);
  paidCount = computed(() => this.payments().filter((p) => p.paidDate).length);
  totalPaid = computed(() =>
    this.payments().filter((p) => p.paidDate).reduce((sum, p) => sum + p.amount, 0),
  );
  balance = computed(() =>
    this.payments().filter((p) => !p.paidDate).reduce((sum, p) => sum + p.amount, 0),
  );
  overdueCount = computed(() => this.payments().filter((p) => p.status === 'Overdue').length);

  constructor() {
    forkJoin({
      enrollment: this.enrollmentsApi.getById(this.enrollmentId),
      payments: this.paymentsApi.getSchedule(this.enrollmentId),
      transactions: this.paymentsApi.getTransactions({ enrollmentId: this.enrollmentId }),
    }).subscribe({
      next: ({ enrollment, payments, transactions }) => {
        this.enrollment.set(enrollment);
        this.payments.set(payments);
        this.transactions.set(transactions);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(getErrorMessage(err));   // e.g. 404 if it isn't your enrollment
        this.loading.set(false);
      },
    });
  }

  readonly methodText = describeMethod;
}
