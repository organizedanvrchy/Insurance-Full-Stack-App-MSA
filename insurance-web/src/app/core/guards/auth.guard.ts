import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

// Guards are UX only (hide pages). The real security is [Authorize] on the API.
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isLoggedIn())
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });

  // After an admin reset, the only page is "choose a new password" (the gateway refuses everything else too).
  return auth.mustChangePassword() ? router.createUrlTree(['/choose-new-password']) : true;
};

/** The "choose a new password" screen: only for a signed-in customer whose password an admin reset. */
export const mustChangePasswordGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isLoggedIn()) return router.createUrlTree(['/login']);
  return auth.mustChangePassword() ? true : router.createUrlTree(['/dashboard']);
};

export const roleGuard =
  (role: 'Admin' | 'Customer'): CanActivateFn =>
  () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    return auth.user()?.role === role ? true : router.createUrlTree(['/dashboard']);
  };
