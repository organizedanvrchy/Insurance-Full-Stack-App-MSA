import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { ThemeToggleComponent } from '../../shared/components/theme-toggle.component';
import { BrandLogoComponent } from '../../shared/components/brand-logo.component';

type State = 'verifying' | 'verified' | 'failed';

/**
 * Opened from the link in a verification email: /verify-email?token=...
 * Works whether or not anyone is signed in on this browser.
 */
@Component({
  selector: 'app-verify-email',
  imports: [RouterLink, ThemeToggleComponent, BrandLogoComponent],
  templateUrl: './verify-email.html',
  styleUrl: '../../shared/styles/centered-card.css',
})
export class VerifyEmailComponent {
  auth = inject(AuthService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  state = signal<State>('verifying');
  email = signal('');
  error = signal<string | null>(null);

  constructor() {
    const token = this.route.snapshot.queryParamMap.get('token');

    // Take the one-time token out of the address bar (and browser history) straight away.
    this.router.navigate([], { queryParams: {}, replaceUrl: true });

    if (!token) {
      this.fail('This link is missing its verification code. Open the link from the email again.');
      return;
    }

    this.auth.verifyEmail(token).subscribe({
      next: (res) => {
        this.email.set(res.email);
        this.state.set('verified');
      },
      error: (err) => this.fail(getErrorMessage(err)),
    });
  }

  private fail(message: string): void {
    this.error.set(message);
    this.state.set('failed');
  }
}
