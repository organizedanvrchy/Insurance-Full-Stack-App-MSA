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
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { PASSWORD_MAX, PASSWORD_RULES, strongPassword } from '../../core/utils/validators';
import { BrandLogoComponent } from '../../shared/components/brand-logo.component';
import { ThemeToggleComponent } from '../../shared/components/theme-toggle.component';

const newPasswordsMatch: ValidatorFn = (g: AbstractControl): ValidationErrors | null =>
  g.get('newPassword')?.value === g.get('confirmNewPassword')?.value ? null : { mismatch: true };

/**
 * After an administrator reset the password, the customer signs in with the one-time password from their email and
 * lands here, and only chooses the new password (signing in with the one-time password was the proof). There's no way into the rest of the app until a new password is chosen (the gateway refuses every other
 * request too); signing out is the only other option.
 */
@Component({
  selector: 'app-choose-new-password',
  imports: [ReactiveFormsModule, BrandLogoComponent, ThemeToggleComponent],
  templateUrl: './choose-new-password.html',
  styleUrls: ['../../shared/styles/centered-card.css', './choose-new-password.css'],
})
export class ChooseNewPasswordComponent {
  auth = inject(AuthService);
  private fb = inject(FormBuilder);
  private router = inject(Router);

  readonly passwordMax = PASSWORD_MAX;
  saving = signal(false);
  error = signal<string | null>(null);

  form = this.fb.nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.maxLength(PASSWORD_MAX), strongPassword]],
      confirmNewPassword: ['', Validators.required],
    },
    { validators: newPasswordsMatch },
  );

  private newValue = toSignal(this.form.controls.newPassword.valueChanges, { initialValue: '' });
  checks = computed(() =>
    PASSWORD_RULES.map((rule) => ({ id: rule.id, label: rule.label, ok: rule.test(this.newValue()) })),
  );

  get f() {
    return this.form.controls;
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);

    this.auth.chooseNewPassword(this.form.getRawValue()).subscribe({
      next: () => this.router.navigate(['/dashboard']),
      error: (err) => {
        this.error.set(getErrorMessage(err)); // the one-time password expired, or the rules aren't met
        this.saving.set(false);
      },
    });
  }

  signOut(): void {
    this.auth.logout();
  }
}
