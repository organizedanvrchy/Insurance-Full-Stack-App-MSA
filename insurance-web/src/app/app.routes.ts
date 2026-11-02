import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  // Public pages (no nav bar)
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    loadComponent: () =>
      import('./features/auth/register.component').then((m) => m.RegisterComponent),
  },

  // Pages that need login and render inside the shell (nav bar + outlet)
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.component').then((m) => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'profile',
        loadComponent: () =>
          import('./features/profiles/profile.component').then((m) => m.ProfileComponent),
      },
      {
        path: 'policies',
        loadComponent: () =>
          import('./features/policies/policy-list.component').then((m) => m.PolicyListComponent),
      },
      {
        path: 'policies/new',
        canActivate: [roleGuard('Admin')],
        loadComponent: () =>
          import('./features/policies/policy-form.component').then((m) => m.PolicyFormComponent),
      },
      {
        path: 'policies/:id/edit',
        canActivate: [roleGuard('Admin')],
        loadComponent: () =>
          import('./features/policies/policy-form.component').then((m) => m.PolicyFormComponent),
      },
      {
        path: 'policies/:id/enroll',
        canActivate: [roleGuard('Customer')],
        loadComponent: () =>
          import('./features/enrollments/enroll.component').then((m) => m.EnrollComponent),
      },
      {
        path: 'my-policies',
        canActivate: [roleGuard('Customer')],
        loadComponent: () =>
          import('./features/enrollments/my-policies.component').then((m) => m.MyPoliciesComponent),
      },
      {
        path: 'my-policies/:id/payments',
        canActivate: [roleGuard('Customer')],
        loadComponent: () =>
          import('./features/payments/payment-schedule.component').then(
            (m) => m.PaymentScheduleComponent,
          ),
      },
      {
        path: 'my-policies/:id/payments/:paymentId/checkout',
        canActivate: [roleGuard('Customer')],
        loadComponent: () =>
          import('./features/payments/checkout.component').then((m) => m.CheckoutComponent),
      },
      {
        // The customer who paid, or an admin from the payment log
        path: 'receipts/:transactionId',
        loadComponent: () =>
          import('./features/payments/receipt.component').then((m) => m.ReceiptComponent),
      },
      {
        path: 'my-policies/:id/claims/new',
        canActivate: [roleGuard('Customer')],
        loadComponent: () =>
          import('./features/claims/claim-form.component').then((m) => m.ClaimFormComponent),
      },
      {
        path: 'claims',
        canActivate: [roleGuard('Customer')],
        loadComponent: () =>
          import('./features/claims/my-claims.component').then((m) => m.MyClaimsComponent),
      },
      {
        path: 'admin/claims',
        canActivate: [roleGuard('Admin')],
        loadComponent: () =>
          import('./features/claims/admin-claims.component').then((m) => m.AdminClaimsComponent),
      },
      {
        path: 'admin/payments',
        canActivate: [roleGuard('Admin')],
        loadComponent: () =>
          import('./features/payments/admin-payments.component').then((m) => m.AdminPaymentsComponent),
      },
      {
        path: 'admin/customers',
        canActivate: [roleGuard('Admin')],
        loadComponent: () =>
          import('./features/customers/admin-customers.component').then(
            (m) => m.AdminCustomersComponent,
          ),
      },
    ],
  },

  // Anything else: 404 page instead of silently redirecting
  {
    path: '**',
    loadComponent: () =>
      import('./features/not-found/not-found.component').then((m) => m.NotFoundComponent),
  },
];
