import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin, map, of, switchMap } from 'rxjs';

import { AuthService } from '../../core/services/auth.service';
import { ClaimService } from '../../core/services/claim.service';
import { EnrollmentService } from '../../core/services/enrollment.service';
import { PaymentService } from '../../core/services/payment.service';
import { ProfileService } from '../../core/services/profile.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { initialsOf } from '../../core/utils/initials';
import { claimBadgeClass, statusLabel } from '../../core/utils/labels';
import { buildMonthBuckets, summarizePayments } from '../../core/utils/payment-stats';
import { PaymentChartComponent } from './payment-chart.component';
import { EmailSettingsComponent } from './email-settings.component';
import { ChangePasswordComponent } from './change-password.component';
import { UserProfile } from '../../core/models/profile/profile.model';
import { Enrollment } from '../../core/models/enrollment/enrollment';
import { Payment } from '../../core/models/payment/payment';
import { Claim } from '../../core/models/claim/claim';
import { OPEN_CLAIM_STATUSES } from '../../core/models/enums';

@Component({
  selector: 'app-profile',
  imports: [RouterLink, CurrencyPipe, DatePipe, PaymentChartComponent, EmailSettingsComponent, ChangePasswordComponent],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.css',
})
export class ProfileComponent {
  auth = inject(AuthService);
  private profileApi = inject(ProfileService);
  private enrollmentsApi = inject(EnrollmentService);
  private paymentsApi = inject(PaymentService);
  private claimsApi = inject(ClaimService);

  readonly initials = initialsOf;
  readonly label = statusLabel;
  readonly badgeClass = claimBadgeClass;

  loading = signal(true);
  error = signal<string | null>(null);

  profile = signal<UserProfile | null>(null);
  enrollments = signal<Enrollment[]>([]);
  payments = signal<Payment[]>([]);
  claims = signal<Claim[]>([]);

  // ---- Everything below is derived from the data above ----
  activePolicies = computed(() => this.enrollments().filter((e) => e.status === 'Active').length);
  summary = computed(() => summarizePayments(this.payments()));
  buckets = computed(() => buildMonthBuckets(this.payments()));
  hasChartData = computed(() => this.buckets().some((b) => b.total > 0));

  recentClaims = computed(() =>
    [...this.claims()].sort((a, b) => b.submittedAt.localeCompare(a.submittedAt)).slice(0, 5),
  );
  openClaims = computed(
    () => this.claims().filter((c) => OPEN_CLAIM_STATUSES.includes(c.status)).length,
  );
  paidOut = computed(() =>
    this.claims().filter((c) => c.status === 'Paid').reduce((sum, c) => sum + c.claimAmount, 0),
  );

  constructor() {
    const profile$ = this.profileApi.getProfile();

    // Admins have no policies, payments or claims: just show their account.
    if (this.auth.isAdmin()) {
      profile$.subscribe({
        next: (p) => {
          this.profile.set(p);
          this.loading.set(false);
        },
        error: (err) => this.fail(err),
      });
      return;
    }

    forkJoin({
      profile: profile$,
      enrollments: this.enrollmentsApi.getMine(),
      claims: this.claimsApi.getMine(),
    })
      .pipe(
        // One schedule request per enrollment. forkJoin([]) would complete without a value, so handle "none" explicitly.
        switchMap((base) => {
          const schedules = base.enrollments.map((e) => this.paymentsApi.getSchedule(e.enrollmentId));
          const lists$ = schedules.length ? forkJoin(schedules) : of([] as Payment[][]);
          return lists$.pipe(map((lists) => ({ ...base, payments: lists.flat() })));
        }),
      )
      .subscribe({
        next: ({ profile, enrollments, claims, payments }) => {
          this.profile.set(profile);
          this.enrollments.set(enrollments);
          this.claims.set(claims);
          this.payments.set(payments);
          this.loading.set(false);
        },
        error: (err) => this.fail(err),
      });
  }

  private fail(err: unknown): void {
    this.error.set(getErrorMessage(err));
    this.loading.set(false);
  }
}
