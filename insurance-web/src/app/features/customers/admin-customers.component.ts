import { DatePipe } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { Customer } from '../../core/models/customer/customer';
import { CustomerService } from '../../core/services/customer.service';
import { getErrorMessage } from '../../core/utils/http-error';
import { CustomerDialogComponent } from './customer-dialog.component';

type StatusFilter = '' | 'active' | 'inactive';

@Component({
  selector: 'app-admin-customers',
  imports: [DatePipe, CustomerDialogComponent],
  templateUrl: "admin-customers.html",
  styleUrl: "admin-customers.component.css",
})
export class AdminCustomersComponent {
  private api = inject(CustomerService);

  customers = signal<Customer[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  selected = signal<Customer | null>(null);
  dialogOpen = signal(false);
  private reloadOnClose = false;

  private search = '';
  private status: StatusFilter = '';

  constructor() {
    this.load();
    effect(() => {
      if (!this.dialogOpen() && this.reloadOnClose) {
        this.reloadOnClose = false;
        this.load();
      }
    });
  }

  applySearch(value: string): void {
    this.search = value.trim();
    this.load();
  }

  setStatus(value: string): void {
    this.status = value as StatusFilter;
    this.load();
  }

  // Clicking a customer opens their pop-up (details, edit, reset password, deactivate / reactivate).
  openCustomer(c: Customer): void {
    this.selected.set(c);
    this.dialogOpen.set(true);
  }

  // A change in the pop-up: refresh that row now, and reload when the pop-up closes so the filters stay accurate.
  onChanged(updated: Customer): void {
    this.customers.update((list) => list.map((c) => (c.userId === updated.userId ? updated : c)));
    this.selected.set(updated);
    this.reloadOnClose = true;
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    const isActive = this.status === '' ? undefined : this.status === 'active';
    this.api.getAll(this.search || undefined, isActive).subscribe({
      next: (list) => {
        this.customers.set(list);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(getErrorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
