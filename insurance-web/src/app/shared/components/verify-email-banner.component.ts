import { Component, inject, input, signal } from '@angular/core';
import { AuthService } from '../../core/services/auth.service';
import { getErrorMessage } from '../../core/utils/http-error';

/**
 * Asks a customer who hasn't verified their email yet to do so, with a "Resend" button.
 * Shown under the nav bar on every page, and inline on the pages that need a verified email (enroll, file a claim).
 */
@Component({
  selector: 'app-verify-email-banner',
  template: `
    @if (auth.needsEmailVerification()) {
      <div class="notice verify" role="status">
        <div>
          <strong>Please verify your email address.</strong>
          We sent a link to <strong>{{ auth.user()?.email }}</strong>.
          @if (action()) {
            You need a verified email to {{ action() }}.
          } @else {
            You can browse plans and get quotes now; enrolling and filing claims unlock once you've verified.
          }
          @if (message(); as m) {
            <span class="result" aria-live="polite">{{ m }}</span>
          }
        </div>
        <div class="buttons">
          <button type="button" class="btn-sm" [disabled]="sending()" (click)="resend()">
            {{ sending() ? 'Sending…' : 'Resend email' }}
          </button>
        </div>
      </div>
    }
  `,
  styles: `
    /* Same width and side padding as .page, so it lines up with the content below */
    :host {
      display: block;
      max-width: 1160px;
      margin: 0 auto;
      padding: 0 1rem;
    }
    :host:has(.verify) {
      padding-top: 1.25rem;
    }
    .verify {
      margin: 0;
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
    }
    .buttons {
      display: flex;
      gap: 0.5rem;
      flex-shrink: 0;
    }
    .result {
      display: block;
      margin-top: 0.35rem;
      font-weight: 600;
    }
  `,
})
export class VerifyEmailBannerComponent {
  auth = inject(AuthService);

  /** Completes "You need a verified email to …", e.g. "enroll in a policy". Empty = the general message. */
  action = input<string>('');

  sending = signal(false);
  message = signal<string | null>(null);

  resend(): void {
    this.sending.set(true);
    this.message.set(null);
    this.auth.resendVerification().subscribe({
      next: () => {
        this.message.set(`Sent! Check your inbox at ${this.auth.user()?.email}.`);
        this.sending.set(false);
      },
      error: (err) => {
        this.message.set(getErrorMessage(err)); // 429: asked too often, with how long to wait
        this.sending.set(false);
      },
    });
  }
}
