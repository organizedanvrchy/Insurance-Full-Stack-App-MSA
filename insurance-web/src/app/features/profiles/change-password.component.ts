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
import { AuthService } from '../../core/services/auth.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { PASSWORD_MAX, PASSWORD_RULES, strongPassword } from '../../core/utils/validators';

const newPasswordsMatch: ValidatorFn = (g: AbstractControl): ValidationErrors | null =>
  g.get('newPassword')?.value === g.get('confirmNewPassword')?.value ? null : { mismatch: true };

/** The "Password" section of the profile page: current password, new password and its confirmation. */
@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule],
  templateUrl: './change-password.component.html',
  styleUrl: './change-password.component.css',
})
export class ChangePasswordComponent {
  private auth = inject(AuthService);
  private fb = inject(FormBuilder);

  readonly passwordMax = PASSWORD_MAX;

  open = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);
  message = signal<string | null>(null);

  form = this.fb.nonNullable.group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, Validators.maxLength(PASSWORD_MAX), strongPassword]],
      confirmNewPassword: ['', Validators.required],
    },
    { validators: newPasswordsMatch },
  );

  // Live checklist under the new password, as on the sign-up form
  private newValue = toSignal(this.form.controls.newPassword.valueChanges, { initialValue: '' });
  checks = computed(() =>
    PASSWORD_RULES.map((rule) => ({ id: rule.id, label: rule.label, ok: rule.test(this.newValue()) })),
  );

  get f() {
    return this.form.controls;
  }

  start(): void {
    this.open.set(true);
    this.message.set(null);
    this.error.set(null);
  }

  cancel(): void {
    this.open.set(false);
    this.form.reset();
    this.error.set(null);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);

    this.auth.changePassword(this.form.getRawValue()).subscribe({
      next: () => {
        this.saving.set(false);
        this.cancel();
        this.message.set('Your password was changed. Use it the next time you sign in.');
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getErrorMessage(err)); // 400: wrong current password, same password, or rules not met
      },
    });
  }
}
