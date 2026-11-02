import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { SentEmail } from '../../core/models/notification/notification';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { notificationKindLabel } from '../../core/utils/labels';

/**
 * The "Email settings" section of the profile page (anchor #email-settings, linked from every reminder email):
 * the address and whether it's verified, changing it, the payment-reminder opt-in, and the emails we've sent.
 */
@Component({
  selector: 'app-email-settings',
  imports: [DatePipe, ReactiveFormsModule],
  templateUrl: './email-settings.component.html',
  styleUrl: './email-settings.component.css',
})
export class EmailSettingsComponent {
  auth = inject(AuthService);
  private notifications = inject(NotificationService);
  private fb = inject(FormBuilder);

  readonly kindLabel = notificationKindLabel;
  readonly isCustomer = computed(() => this.auth.user()?.role === 'Customer');

  // ---- Verification ----
  sending = signal(false);
  verifyMessage = signal<string | null>(null);
  verifyError = signal<string | null>(null);

  // ---- Changing the email ----
  changeOpen = signal(false);
  changing = signal(false);
  changeError = signal<string | null>(null);
  changeForm = this.fb.nonNullable.group({
    newEmail: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    currentPassword: ['', Validators.required],
  });

  get c() {
    return this.changeForm.controls;
  }

  // ---- Preferences and history (customers only) ----
  paymentReminders = signal(true);
  prefsLoaded = signal(false);
  prefsSaving = signal(false);
  prefsMessage = signal<string | null>(null);
  history = signal<SentEmail[]>([]);
  historyLoaded = signal(false);

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
    this.loadHistory();
  }

  resend(): void {
    this.sending.set(true);
    this.verifyMessage.set(null);
    this.verifyError.set(null);
    this.auth.resendVerification().subscribe({
      next: () => {
        this.sending.set(false);
        this.verifyMessage.set(`We sent a new link to ${this.auth.user()?.email}.`);
        this.loadHistory();
      },
      error: (err) => {
        this.sending.set(false);
        this.verifyError.set(getErrorMessage(err)); // 429: asked too often
      },
    });
  }

  openChange(): void {
    this.changeOpen.set(true);
    this.changeError.set(null);
    this.verifyMessage.set(null);
    this.verifyError.set(null);
  }

  closeChange(): void {
    this.changeOpen.set(false);
    this.changeForm.reset();
  }

  submitChange(): void {
    if (this.changeForm.invalid) {
      this.changeForm.markAllAsTouched();
      return;
    }
    this.changing.set(true);
    this.changeError.set(null);

    const oldEmail = this.auth.user()?.email;
    this.auth.changeEmail(this.changeForm.getRawValue()).subscribe({
      next: (user) => {
        this.changing.set(false);
        this.closeChange();
        this.verifyMessage.set(
          `Your email is now ${user.email}. We sent a verification link there, and a notice to ${oldEmail}.`,
        );
        this.loadHistory();
      },
      error: (err) => {
        this.changing.set(false);
        this.changeError.set(getErrorMessage(err)); // 400 wrong password / same address, 409 taken, 429 too many
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

  private loadHistory(): void {
    if (!this.isCustomer()) return;
    this.notifications.getHistory(10).subscribe({
      next: (h) => {
        this.history.set(h);
        this.historyLoaded.set(true);
      },
      error: () => this.historyLoaded.set(true),
    });
  }
}
