import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { API_URL } from '../api.config';
import { User } from '../models/auth/user';
import { RegisterRequest } from '../models/auth/registerRequest';
import { AuthResponse } from '../models/auth/authResponse';
import { LoginRequest } from '../models/auth/loginRequest';
import {
  ChangeEmailRequest,
  ChangePasswordRequest,
  VerifyEmailResponse,
} from '../models/auth/emailVerification';

const TOKEN_KEY = 'ins_token';
const USER_KEY = 'ins_user';
const EXP_KEY = 'ins_exp';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);

  private _user = signal<User | null>(this.restoreSession());
  readonly user = this._user.asReadonly();
  readonly isLoggedIn = computed(() => this._user() !== null);
  readonly isAdmin = computed(() => this._user()?.role === 'Admin');

  /** A signed-in customer who hasn't clicked the link in their verification email yet. */
  readonly needsEmailVerification = computed(() => {
    const user = this._user();
    return user !== null && user.role === 'Customer' && user.emailVerified === false;
  });

  constructor() {
    // Keep tabs in step: verifying in the tab the email link opened updates the banner in every other tab.
    window.addEventListener('storage', (e) => {
      if (e.key !== USER_KEY) return;
      if (e.newValue === null) this._user.set(null);
      else this._user.set(JSON.parse(e.newValue) as User);
    });
  }

  get token(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  register(req: RegisterRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_URL}/auth/register`, req)
      .pipe(tap((res) => this.setSession(res)));
  }

  login(req: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_URL}/auth/login`, req)
      .pipe(tap((res) => this.setSession(res)));
  }

  // ---------------- Email verification ----------------

  /** Uses the token from the emailed link. Works whether or not anyone is signed in. */
  verifyEmail(token: string): Observable<VerifyEmailResponse> {
    return this.http
      .post<VerifyEmailResponse>(`${API_URL}/auth/verify-email`, { token })
      .pipe(tap(() => this.refreshUser()));
  }

  resendVerification(): Observable<void> {
    return this.http.post<void>(`${API_URL}/auth/resend-verification`, {});
  }

  // ---------------- Changing the email and password ----------------

  /** Takes effect at once; the new address is unverified until its link is used, and the old one is told. */
  changeEmail(req: ChangeEmailRequest): Observable<User> {
    return this.http
      .post<User>(`${API_URL}/auth/change-email`, req)
      .pipe(tap((user) => this.updateUser(user)));
  }

  changePassword(req: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>(`${API_URL}/auth/change-password`, req);
  }

  /**
   * Reloads the signed-in user from the server (e.g. after they verified their email in another tab).
   * Errors are ignored: a 401 is already handled by the interceptor.
   */
  refreshUser(): void {
    if (!this.token) return;
    this.http.get<User>(`${API_URL}/auth/me`).subscribe({
      next: (user) => this.updateUser(user),
      error: () => {},
    });
  }

  private updateUser(user: User): void {
    if (!this._user()) return;
    localStorage.setItem(USER_KEY, JSON.stringify(user));
    this._user.set(user);
  }

  logout(): void {
    this.clearSession();
    this.router.navigate(['/login']);
  }

  private setSession(res: AuthResponse): void {
    localStorage.setItem(TOKEN_KEY, res.token);
    localStorage.setItem(USER_KEY, JSON.stringify(res.user));
    localStorage.setItem(EXP_KEY, res.expiresAtUtc);
    this._user.set(res.user);
  }

  private clearSession(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    localStorage.removeItem(EXP_KEY);
    this._user.set(null);
  }

  /** Runs on page refresh so the user stays logged in until the token expires. */
  private restoreSession(): User | null {
    try {
      const raw = localStorage.getItem(USER_KEY);
      const exp = localStorage.getItem(EXP_KEY);
      if (!raw || !exp || Date.parse(exp) < Date.now()) {
        localStorage.removeItem(TOKEN_KEY);
        localStorage.removeItem(USER_KEY);
        localStorage.removeItem(EXP_KEY);
        return null;
      }
      return JSON.parse(raw) as User;
    } catch {
      return null;
    }
  }
}
