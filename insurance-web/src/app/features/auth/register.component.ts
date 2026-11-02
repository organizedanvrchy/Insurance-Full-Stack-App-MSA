import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { US_STATES } from '../../core/models/us-states';
import { AuthService } from '../../core/services/auth.service';
import { getErrorMessage } from '../../core/utils/http-error';
import {
  EMAIL_PATTERN,
  NAME_PATTERN,
  PASSWORD_MAX,
  PASSWORD_RULES,
  ZIP_PATTERN,
  normalizeUsPhone,
  strongPassword,
  usPhone,
} from '../../core/utils/validators';
import { AuthBrandPanelComponent } from '../../shared/components/auth-brand-panel.component';
import { BrandLogoComponent } from '../../shared/components/brand-logo.component';
import { ThemeToggleComponent } from '../../shared/components/theme-toggle.component';

const passwordsMatch: ValidatorFn = (g: AbstractControl): ValidationErrors | null =>
  g.get('password')?.value === g.get('confirmPassword')?.value ? null : { mismatch: true };

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, ThemeToggleComponent, AuthBrandPanelComponent, BrandLogoComponent],
  templateUrl: './register.html',
  styleUrls: ['../../shared/styles/centered-card.css', './register.css'],
})
export class RegisterComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private router = inject(Router);

  readonly states = US_STATES;
  readonly passwordMax = PASSWORD_MAX;

  loading = signal(false);
  error = signal<string | null>(null);

  form = this.fb.nonNullable.group(
    {
      firstName: ['', [Validators.required, Validators.maxLength(45), Validators.pattern(NAME_PATTERN)]],
      lastName: ['', [Validators.required, Validators.maxLength(45), Validators.pattern(NAME_PATTERN)]],
      email: ['', [Validators.required, Validators.email, Validators.pattern(EMAIL_PATTERN)]],
      phone: ['', [usPhone]],
      street: ['', [Validators.required, Validators.maxLength(100)]],
      unit: ['', [Validators.maxLength(40)]],
      city: ['', [Validators.required, Validators.maxLength(60), Validators.pattern(NAME_PATTERN)]],
      state: ['', [Validators.required]],
      postalCode: ['', [Validators.required, Validators.pattern(ZIP_PATTERN)]],
      password: ['', [Validators.required, Validators.maxLength(PASSWORD_MAX), strongPassword]],
      confirmPassword: ['', Validators.required],
      paymentReminders: [true],   // "Email me payment reminders": changeable later on the profile page
    },
    { validators: passwordsMatch },
  );

  // Live checklist under the password field
  private passwordValue = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });
  checks = computed(() =>
    PASSWORD_RULES.map((rule) => ({ id: rule.id, label: rule.label, ok: rule.test(this.passwordValue()) })),
  );

  get f() {
    return this.form.controls;
  }

  /** On leaving the phone field, tidy a valid number into (203) 555-0141. */
  formatPhone(): void {
    const formatted = normalizeUsPhone(this.f.phone.value);
    if (formatted) this.f.phone.setValue(formatted);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.loading.set(true);
    this.error.set(null);

    const v = this.form.getRawValue();
    this.auth
      .register({
        firstName: v.firstName.trim(),
        lastName: v.lastName.trim(),
        email: v.email,
        password: v.password,
        phone: normalizeUsPhone(v.phone) ?? undefined,   // empty or invalid-looking numbers are not sent
        street: v.street.trim(),
        unit: v.unit.trim() || undefined,
        city: v.city.trim(),
        state: v.state,
        postalCode: v.postalCode.trim(),
        paymentReminders: v.paymentReminders,
      })
      .subscribe({
        next: () => this.router.navigate(['/dashboard']),   // API already returned a token = logged in
        error: (err) => {
          this.error.set(getErrorMessage(err));              // e.g. 409 "That email is already registered."
          this.loading.set(false);
        },
      });
  }
}
