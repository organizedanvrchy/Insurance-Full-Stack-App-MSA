import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { ClaimService } from '../../core/services/claim.service';
import { EnrollmentService } from '../../core/services/enrollment.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { Enrollment } from '../../core/models/enrollment/enrollment';

@Component({
  selector: 'app-claim-form',
  imports: [ReactiveFormsModule, RouterLink, CurrencyPipe, DatePipe],
  templateUrl: "claim-form.html",
})
export class ClaimFormComponent {
  private fb = inject(FormBuilder);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private enrollmentsApi = inject(EnrollmentService);
  private claimsApi = inject(ClaimService);
  auth = inject(AuthService);

  private readonly enrollmentId = Number(this.route.snapshot.paramMap.get('id'));
  private readonly today = new Date().toLocaleDateString('en-CA');   // local YYYY-MM-DD

  enrollment = signal<Enrollment | null>(null);
  saving = signal(false);
  error = signal<string | null>(null);

  // Latest allowed incident date: today, or the end of coverage if that has already passed.
  // (ISO "YYYY-MM-DD" strings compare correctly as plain text.)
  windowEnd = computed(() => {
    const e = this.enrollment();
    return e && e.endDate < this.today ? e.endDate : this.today;
  });

  // False when coverage starts in the future, so there is no valid incident date yet.
  windowOpen = computed(() => {
    const e = this.enrollment();
    return !!e && e.startDate <= this.windowEnd();
  });

  form = this.fb.group({
    incidentDate: ['', Validators.required],
    claimAmount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    description: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(2000)]],
  });

  get f() {
    return this.form.controls;
  }

  constructor() {
    this.enrollmentsApi.getById(this.enrollmentId).subscribe({
      next: (e) => {
        this.enrollment.set(e);

        // If exactly one date is allowed (policy started today), pre-fill it.
        if (e.startDate === this.windowEnd()) {
          this.form.patchValue({ incidentDate: e.startDate });
        }
      },
      error: (err) => this.error.set(getErrorMessage(err)),   // 404 if it isn't yours
    });
  }

  submit(e: Enrollment): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);

    const v = this.form.getRawValue();
    this.claimsApi
      .submit({
        enrollmentId: e.enrollmentId,
        incidentDate: v.incidentDate!,
        claimAmount: v.claimAmount!,
        description: v.description!,
      })
      .subscribe({
        next: () => this.router.navigate(['/claims']),
        error: (err) => {
          this.error.set(getErrorMessage(err));   // 409 overdue premiums, 400 amount/date rules
          this.saving.set(false);
        },
      });
  }
}
