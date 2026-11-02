import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';
import { SentEmail } from '../../core/models/notification/notification';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { notificationKindLabel } from '../../core/utils/labels';

/**
 * The "Email settings" section of the profile page (anchor #email-settings, linked from every reminder email):
 * the address and whether it's verified, changing it, payment-reminder opt-out, and the emails we've sent.
 */
@Component({
  selector: 'app-email-settings',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './email-settings.component.html',
  styleUrl: './email-settings.component.css',
})
export class EmailSettingsComponent {
  auth = inject(AuthService);
  private notifications = inject(NotificationService);
  private fb = inject(FormBuilder);

  readonly kindLabel = notificationKindLabel;
  readonly isCustomer = computed(() => this.auth.user()?.role === 'Customer');

  // ---- Address and verification ----
  busy = signal(false);
  addressMessage = signal<string | null>(null);
  addressError = signal<string | null>(null);

  // ---- Change email ----
  changing = signal(false);
  changeOpen = signal(false);
  changeError = signal<string | null>(null);
  changeForm = this.fb.nonNullable.group({
    newEmail: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    currentPassword: ['', Validators.required],
  });

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

  get c() {
    return this.changeForm.controls;
  }

  resend(): void {
    this.run(this.auth.resendVerification(), () => {
      const to = this.auth.user()?.pendingEmail ?? this.auth.user()?.email;
      this.addressMessage.set(`We sent a new link to ${to}.`);
    });
  }

  cancelChange(): void {
    this.run(this.auth.cancelEmailChange(), () => this.addressMessage.set('The email change was cancelled.'));
  }

  openChange(): void {
    this.changeOpen.set(true);
    this.changeError.set(null);
    this.addressMessage.set(null);
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

    this.auth.changeEmail(this.changeForm.getRawValue()).subscribe({
      next: (user) => {
        this.changing.set(false);
        this.closeChange();
        this.addressMessage.set(
          `We sent a confirmation link to ${user.pendingEmail}. Your current address keeps working until you click it.`,
        );
        this.loadHistory();
      },
      error: (err) => {
        this.changeError.set(getErrorMessage(err)); // 400 wrong password, 409 address taken, 429 too many
        this.changing.set(false);
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
    this.notifications.getHistory(10).subscribe({
      next: (h) => {
        this.history.set(h);
        this.historyLoaded.set(true);
      },
      error: () => this.historyLoaded.set(true),
    });
  }

  private run(call: Observable<unknown>, done: () => void): void {
    this.busy.set(true);
    this.addressMessage.set(null);
    this.addressError.set(null);
    call.subscribe({
      next: () => {
        this.busy.set(false);
        done();
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.addressError.set(getErrorMessage(err));
      },
    });
  }
}
