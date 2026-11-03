import { DatePipe } from '@angular/common';
import { Component, effect, inject, input, model, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';
import { Customer, CustomerDetailsUpdate } from '../../core/models/customer/customer';
import { US_STATES } from '../../core/models/us-states';
import { CustomerService } from '../../core/services/customer.service';
import { splitAddress, splitName } from '../../core/utils/contact-parts';
import { getErrorMessage } from '../../core/utils/http-error';
import { EMAIL_PATTERN, NAME_PATTERN, ZIP_PATTERN, normalizeUsPhone, usPhone } from '../../core/utils/validators';
import { ModalComponent } from '../../shared/components/modal.component';

/** What the admin asked for; each one is confirmed (Confirm / Cancel) before anything is sent. */
type Pending = 'save' | 'reset' | 'deactivate' | 'reactivate';

/**
 * The admin's customer pop-up: the customer's details and activity at a glance, an edit form (name, email, phone,
 * address), and the account actions (reset password, deactivate / reactivate). Every change asks "Are you sure?" first.
 */
@Component({
  selector: 'app-customer-dialog',
  imports: [ReactiveFormsModule, DatePipe, ModalComponent],
  templateUrl: './customer-dialog.html',
  styleUrl: './customer-dialog.css',
})
export class CustomerDialogComponent {
  private api = inject(CustomerService);
  private fb = inject(FormBuilder);

  readonly states = US_STATES;

  open = model(false);
  customer = input<Customer | null>(null);
  /** The customer after a change, so the list can refresh its row. */
  changed = output<Customer>();

  current = signal<Customer | null>(null);
  pending = signal<Pending | null>(null);
  busy = signal(false);
  error = signal<string | null>(null);
  message = signal<string | null>(null);

  form = this.fb.nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(45), Validators.pattern(NAME_PATTERN)]],
    lastName: ['', [Validators.required, Validators.maxLength(45), Validators.pattern(NAME_PATTERN)]],
    email: ['', [Validators.required, Validators.email, Validators.pattern(EMAIL_PATTERN), Validators.maxLength(256)]],
    phone: ['', [usPhone]],
    street: ['', [Validators.required, Validators.maxLength(100)]],
    unit: ['', [Validators.maxLength(40)]],
    city: ['', [Validators.required, Validators.maxLength(60), Validators.pattern(NAME_PATTERN)]],
    state: ['', [Validators.required]],
    postalCode: ['', [Validators.required, Validators.pattern(ZIP_PATTERN)]],
  });

  constructor() {
    // A customer was picked: start from their saved details, with nothing pending.
    effect(() => {
      const c = this.customer();
      if (c && this.open()) this.load(c);
    });
  }

  get f() {
    return this.form.controls;
  }

  private load(c: Customer): void {
    this.current.set(c);
    this.pending.set(null);
    this.error.set(null);
    this.message.set(null);
    this.form.reset({ ...splitName(c.fullName), email: c.email, phone: c.phone ?? '', ...splitAddress(c.address) });
  }

  // ---- Step 1: what the admin wants ----

  askSave(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (!this.form.dirty) {
      this.error.set('Change at least one field first.');
      return;
    }
    this.ask('save');
  }

  ask(action: Pending): void {
    this.error.set(null);
    this.message.set(null);
    this.pending.set(action);
  }

  cancelPending(): void {
    this.pending.set(null);
  }

  /** Whether the save changes the email (the customer then has to verify it again). */
  emailChanges(): boolean {
    return this.f.email.value.trim().toLowerCase() !== this.current()?.email;
  }

  // ---- Step 2: confirmed ----

  confirm(): void {
    const c = this.current();
    const action = this.pending();
    if (!c || !action) return;

    const call: Observable<Customer> =
      action === 'save' ? this.api.update(c.userId, this.details())
      : action === 'reset' ? this.api.resetPassword(c.userId)
      : this.api.setActive(c.userId, action === 'reactivate');

    this.busy.set(true);
    call.subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.pending.set(null);
        this.current.set(updated);
        this.form.reset({ ...splitName(updated.fullName), email: updated.email, phone: updated.phone ?? '', ...splitAddress(updated.address) });
        this.message.set(this.doneMessage(action, c, updated));
        this.changed.emit(updated);
      },
      error: (err) => {
        this.busy.set(false);
        this.pending.set(null);
        this.error.set(getErrorMessage(err)); // 400 invalid, 409 email taken
      },
    });
  }

  private details(): CustomerDetailsUpdate {
    const v = this.form.getRawValue();
    return {
      firstName: v.firstName.trim(),
      lastName: v.lastName.trim(),
      email: v.email.trim(),
      phone: normalizeUsPhone(v.phone) ?? undefined,
      street: v.street.trim(),
      unit: v.unit.trim() || undefined,
      city: v.city.trim(),
      state: v.state,
      postalCode: v.postalCode.trim(),
    };
  }

  private doneMessage(action: Pending, before: Customer, after: Customer): string {
    switch (action) {
      case 'save':
        return before.email !== after.email
          ? `Saved. ${after.email} has been sent a verification link, and ${before.email} has been told about the change.`
          : 'Saved.';
      case 'reset':
        return `A one-time password has been emailed to ${after.email}. It works for 30 minutes.`;
      case 'deactivate':
        return 'Account deactivated. They can no longer sign in.';
      case 'reactivate':
        return 'Account reactivated. They can sign in again.';
    }
  }
}
