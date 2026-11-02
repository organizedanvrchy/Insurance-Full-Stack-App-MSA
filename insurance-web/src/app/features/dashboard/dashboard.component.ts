import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { BRAND } from '../../core/brand';
import { CLAIM_STATUSES, ClaimStatus, OPEN_CLAIM_STATUSES } from '../../core/models/enums';
import { AuthService } from '../../core/services/auth.service';
import { ClaimService } from '../../core/services/claim.service';
import { CustomerService } from '../../core/services/customer.service';
import { EnrollmentService } from '../../core/services/enrollment.service';
import { PolicyService } from '../../core/services/policy.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { claimBadgeClass, statusLabel } from '../../core/utils/labels';
import { Enrollment } from '../../core/models/enrollment/enrollment';
import { Claim } from '../../core/models/claim/claim';
import { Customer } from '../../core/models/customer/customer';
import { Policy } from '../../core/models/policy/policy';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, CurrencyPipe, DatePipe],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.css',
})
export class DashboardComponent {
  auth = inject(AuthService);
  private enrollmentsApi = inject(EnrollmentService);
  private claimsApi = inject(ClaimService);
  private customersApi = inject(CustomerService);
  private policiesApi = inject(PolicyService);

  readonly slogan = BRAND.slogan;
  readonly label = statusLabel;
  readonly badgeClass = claimBadgeClass;

  readonly greeting = (() => {
    const hour = new Date().getHours();
    return hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
  })();

  loading = signal(true);
  error = signal<string | null>(null);

  // Data for customers
  enrollments = signal<Enrollment[]>([]);
  myClaims = signal<Claim[]>([]);

  // Data for admins
  allClaims = signal<Claim[]>([]);
  customers = signal<Customer[]>([]);
  plans = signal<Policy[]>([]);

  firstName = computed(() => this.auth.user()?.fullName.split(' ')[0] ?? '');

  // Customer overview (everything is
  // derived from the lists above)
  activeEnrollments = computed(() => this.enrollments().filter((e) => e.status === 'Active'));
  monthlyTotal = computed(() => this.activeEnrollments().reduce((sum, e) => sum + e.monthlyPremium, 0));
  nextPayment = computed(
    () =>
      this.activeEnrollments()
        .filter((e) => e.nextDueDate)
        .sort((a, b) => a.nextDueDate!.localeCompare(b.nextDueDate!))[0] ?? null,
  );
  overdue = computed(() => this.activeEnrollments().filter((e) => e.paymentAlert === 'Overdue'));
  dueSoon = computed(() => this.activeEnrollments().filter((e) => e.paymentAlert === 'DueSoon'));
  openClaims = computed(
    () => this.myClaims().filter((c) => OPEN_CLAIM_STATUSES.includes(c.status)).length,
  );
  claimUpdates = computed(() =>
    this.myClaims()
      .filter((c) => c.reviewedAt)
      .sort((a, b) => b.reviewedAt!.localeCompare(a.reviewedAt!))
      .slice(0, 4),
  );

  // Admin overview
  statusCounts = computed(() =>
    CLAIM_STATUSES.map((status) => ({
      status,
      count: this.allClaims().filter((c) => c.status === status).length,
    })),
  );
  queue = computed(() =>
    this.allClaims()
      .filter((c) => c.status === 'Submitted')
      .sort((a, b) => a.submittedAt.localeCompare(b.submittedAt))
      .slice(0, 5),
  );
  activeCustomers = computed(() => this.customers().filter((c) => c.isActive).length);

  countOf(status: ClaimStatus): number {
    return this.statusCounts().find((s) => s.status === status)?.count ?? 0;
  }

  constructor() {
    if (this.auth.isAdmin()) {
      forkJoin({
        claims: this.claimsApi.getAll(),
        customers: this.customersApi.getAll(),
        plans: this.policiesApi.getAll(),
      }).subscribe({
        next: ({ claims, customers, plans }) => {
          this.allClaims.set(claims);
          this.customers.set(customers);
          this.plans.set(plans);
          this.loading.set(false);
        },
        error: (err) => this.fail(err),
      });
    } else {
      forkJoin({
        enrollments: this.enrollmentsApi.getMine(),
        claims: this.claimsApi.getMine(),
      }).subscribe({
        next: ({ enrollments, claims }) => {
          this.enrollments.set(enrollments);
          this.myClaims.set(claims);
          this.loading.set(false);
        },
        error: (err) => this.fail(err),
      });
    }
  }

  private fail(err: unknown): void {
    this.error.set(getErrorMessage(err));
    this.loading.set(false);
  }
}
