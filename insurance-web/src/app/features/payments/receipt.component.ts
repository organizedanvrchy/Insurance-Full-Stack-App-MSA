import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { switchMap, tap } from 'rxjs';
import { Enrollment } from '../../core/models/enrollment/enrollment';
import { Transaction } from '../../core/models/payment/checkout';
import { describeMethod } from '../../core/payments/method-labels';
import { AuthService } from '../../core/services/auth.service';
import { EnrollmentService } from '../../core/services/enrollment.service';
import { PaymentService } from '../../core/services/payment.service';
import { getErrorMessage } from '../../core/utils/http-error';

/** A printable receipt for one payment attempt (the customer who paid, or an admin). */
@Component({
  selector: 'app-receipt',
  imports: [RouterLink, CurrencyPipe, DatePipe],
  templateUrl: 'receipt.html',
  styleUrl: 'receipt.component.css',
})
export class ReceiptComponent {
  private route = inject(ActivatedRoute);
  private paymentsApi = inject(PaymentService);
  private enrollmentsApi = inject(EnrollmentService);
  readonly auth = inject(AuthService);

  transaction = signal<Transaction | null>(null);
  enrollment = signal<Enrollment | null>(null);
  error = signal<string | null>(null);

  constructor() {
    const id = Number(this.route.snapshot.paramMap.get('transactionId'));
    this.paymentsApi
      .getTransaction(id)
      .pipe(
        tap((t) => this.transaction.set(t)),
        switchMap((t) => this.enrollmentsApi.getById(t.enrollmentId)),
      )
      .subscribe({
        next: (e) => this.enrollment.set(e),
        error: (err) => this.error.set(getErrorMessage(err)),
      });
  }

  readonly methodText = describeMethod;

  print(): void {
    window.print();
  }
}
