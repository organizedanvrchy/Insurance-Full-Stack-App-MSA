import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { EMAIL_PATTERN } from '../../core/utils/validators';

type ChangeStep = 'closed' | 'edit' | 'confirm';

/**
 * The "Email settings" section of the profile page (anchor #email-settings, linked from every reminder email):
 * the address and whether it's verified, changing it, and the payment-reminder opt-in.
 */
@Component({
  selector: 'app-email-settings',
  imports: [ReactiveFormsModule],
  templateUrl: './email-settings.component.html',
  styleUrl: './email-settings.component.css',
})
export class EmailSettingsComponent {
  auth = inject(AuthService);
  private notifications = inject(NotificationService);
  private fb = inject(FormBuilder);

  readonly isCustomer = computed(() => this.auth.user()?.role === 'Customer');

  // ---- Verification ----
  sending = signal(false);
  verifyMessage = signal<string | null>(null);
  verifyError = signal<string | null>(null);

  // ---- Changing the email: enter the address, then "Are you sure?" ----
  step = signal<ChangeStep>('closed');
  changing = signal(false);
  changeError = signal<string | null>(null);
  changeForm = this.fb.nonNullable.group({
    newEmail: ['', [Validators.required, Validators.email, Validators.pattern(EMAIL_PATTERN), Validators.maxLength(256)]],
  });

  get newEmail() {
    return this.changeForm.controls.newEmail;
  }

  // ---- Preferences (customers only) ----
  paymentReminders = signal(true);
  prefsLoaded = signal(false);
  prefsSaving = signal(false);
  prefsMessage = signal<string | null>(null);

  constructor() {
    this.auth.refreshUser();
    if (!this.isCustomer()) return;

    this.notifications.getPreferences().subscribe({
      next: (p) => {
        this.paymentReminders.set(p.paymentReminders);
        this.prefsLoaded.set(true);
      },
      error: (err) => this.prefsMessage.set(getErrorMessage(err)),
    });
  }

  resend(): void {
    this.sending.set(true);
    this.verifyMessage.set(null);
    this.verifyError.set(null);
    this.auth.resendVerification().subscribe({
      next: () => {
        this.sending.set(false);
        this.verifyMessage.set(`We sent a new link to ${this.auth.user()?.email}. It works for 15 minutes.`);
      },
      error: (err) => {
        this.sending.set(false);
        this.verifyError.set(getErrorMessage(err)); // 429: asked too often
      },
    });
  }

  openChange(): void {
    this.step.set('edit');
    this.changeError.set(null);
    this.verifyMessage.set(null);
    this.verifyError.set(null);
  }

  closeChange(): void {
    this.step.set('closed');
    this.changeForm.reset();
    this.changeError.set(null);
  }

  /** Step 1: a valid, different address. Then ask "Are you sure?". */
  review(): void {
    if (this.newEmail.invalid) {
      this.newEmail.markAsTouched();
      return;
    }
    if (this.newEmail.value.trim().toLowerCase() === this.auth.user()?.email) {
      this.changeError.set('That is already your email address.');
      return;
    }
    this.changeError.set(null);
    this.step.set('confirm');
  }

  /** Step 2: confirmed. */
  confirmChange(): void {
    this.changing.set(true);
    this.changeError.set(null);

    const oldEmail = this.auth.user()?.email;
    this.auth.changeEmail({ newEmail: this.newEmail.value.trim() }).subscribe({
      next: (user) => {
        this.changing.set(false);
        this.closeChange();
        this.verifyMessage.set(
          `Your email is now ${user.email}. We sent a verification link there (it works for 15 minutes), and a notice to ${oldEmail}.`,
        );
      },
      error: (err) => {
        this.changing.set(false);
        this.step.set('edit');
        this.changeError.set(getErrorMessage(err)); // 400 invalid/same address, 409 taken, 429 too many
      },
    });
  }

  toggleReminders(enabled: boolean): void {
    const previous = this.paymentReminders();
    this.paymentReminders.set(enabled);
    this.prefsSaving.set(true);
    this.prefsMessage.set(null);

    this.notifications.updatePreferences({ paymentReminders: enabled }).subscribe({
      next: (p) => {
        this.paymentReminders.set(p.paymentReminders);
        this.prefsSaving.set(false);
        this.prefsMessage.set(p.paymentReminders ? 'Payment reminders are on.' : 'Payment reminders are off.');
      },
      error: (err) => {
        this.paymentReminders.set(previous); // undo the optimistic change
        this.prefsSaving.set(false);
        this.prefsMessage.set(getErrorMessage(err));
      },
    });
  }
}
