import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { SentEmail } from '../../core/models/notification/notification';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { notificationKindLabel } from '../../core/utils/labels';

/**
 * The "Email settings" section of the profile page (anchor #email-settings, linked from every reminder email):
 * the address and whether it's verified, the payment-reminder opt-in, and the emails we've sent.
 */
@Component({
  selector: 'app-email-settings',
  imports: [DatePipe],
  templateUrl: './email-settings.component.html',
  styleUrl: './email-settings.component.css',
})
export class EmailSettingsComponent {
  auth = inject(AuthService);
  private notifications = inject(NotificationService);

  readonly kindLabel = notificationKindLabel;
  readonly isCustomer = computed(() => this.auth.user()?.role === 'Customer');

  // ---- Verification ----
  sending = signal(false);
  verifyMessage = signal<string | null>(null);
  verifyError = signal<string | null>(null);

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
