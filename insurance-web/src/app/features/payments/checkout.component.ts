import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { PAYMENT_METHOD_TYPES, PaymentMethodType } from '../../core/models/enums';
import { BRAND } from '../../core/brand';
import { Enrollment } from '../../core/models/enrollment/enrollment';
import { CheckoutRequest, SavedPaymentMethod } from '../../core/models/payment/checkout';
import { Payment } from '../../core/models/payment/payment';
import { UserProfile } from '../../core/models/profile/profile.model';
import { US_STATES } from '../../core/models/us-states';
import {
  BRAND_LABELS,
  CardBrand,
  cardLength,
  cvcLength,
  detectBrand,
  digitsOnly,
  formatCardNumber,
  formatExpiry,
  parseExpiry,
  passesLuhn,
} from '../../core/payments/card-utils';
import { METHOD_LABELS } from '../../core/payments/method-labels';
import { TEST_CARDS } from '../../core/payments/mock-payment-provider';
import { PaymentProvider } from '../../core/payments/payment-provider';
import { EnrollmentService } from '../../core/services/enrollment.service';
import { PaymentService } from '../../core/services/payment.service';
import { ProfileService } from '../../core/services/profile.service';
import { parseAddress } from '../../core/utils/address';
import { getErrorMessage } from '../../core/utils/http-error';

type WalletType = Exclude<PaymentMethodType, 'Card'>;

/** The method tabs, in display order. */
const METHODS = PAYMENT_METHOD_TYPES.map((type) => ({ type, label: METHOD_LABELS[type] }));

/** A new value for every payment attempt; sending the same one again is a retry, never a second charge. */
const newAttemptKey = () => crypto.randomUUID();

@Component({
  selector: 'app-checkout',
  imports: [ReactiveFormsModule, RouterLink, CurrencyPipe, DatePipe],
  templateUrl: 'checkout.html',
  styleUrl: 'checkout.component.css',
})
export class CheckoutComponent {
  private fb = inject(FormBuilder);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private enrollmentsApi = inject(EnrollmentService);
  private paymentsApi = inject(PaymentService);
  private profileApi = inject(ProfileService);
  readonly provider = inject(PaymentProvider);

  readonly enrollmentId = Number(this.route.snapshot.paramMap.get('id'));
  private readonly paymentId = Number(this.route.snapshot.paramMap.get('paymentId'));

  readonly methods = METHODS;
  readonly states = US_STATES;
  readonly testCards = TEST_CARDS;
  readonly brandLabels = BRAND_LABELS;
  readonly merchant = BRAND.name;

  enrollment = signal<Enrollment | null>(null);
  schedule = signal<Payment[]>([]);
  savedMethods = signal<SavedPaymentMethod[]>([]);
  profile = signal<UserProfile | null>(null);
  loading = signal(true);
  error = signal<string | null>(null);
  declined = signal<{ message: string; reference: string } | null>(null);
  addressFromProfile = signal(false);

  method = signal<PaymentMethodType>('Card');
  savedMethodId = signal<number | null>(null);   // null = enter a new card
  brand = signal<CardBrand | null>(null);
  processing = signal<'idle' | 'card' | 'wallet' | 'charging'>('idle');
  walletPrompt = signal<WalletType | null>(null);

  private attemptKey = newAttemptKey();

  installment = computed(() => this.schedule().find((p) => p.paymentId === this.paymentId) ?? null);
  installmentNumber = computed(() => this.schedule().findIndex((p) => p.paymentId === this.paymentId) + 1);
  isNextDue = computed(() => this.schedule().find((p) => !p.paidDate)?.paymentId === this.paymentId);
  savedCards = computed(() => this.savedMethods().filter((m) => m.methodType === 'Card'));
  walletHandle = computed(() => '@' + (this.profile()?.email.split('@')[0] ?? 'you'));

  form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    number: ['', [Validators.required, (c: AbstractControl) => this.cardNumberError(c)]],
    expiry: ['', [Validators.required, (c: AbstractControl) => (parseExpiry(c.value) ? null : { expiry: true })]],
    cvc: ['', [Validators.required, (c: AbstractControl) => this.cvcError(c)]],
    street: ['', [Validators.required, Validators.maxLength(100)]],
    unit: [''],
    city: ['', [Validators.required, Validators.maxLength(60)]],
    state: ['', Validators.required],
    postalCode: ['', [Validators.required, Validators.pattern(/^\d{5}(-\d{4})?$/)]],
    saveCard: [false],
  });

  get f() {
    return this.form.controls;
  }

  constructor() {
    forkJoin({
      enrollment: this.enrollmentsApi.getById(this.enrollmentId),
      schedule: this.paymentsApi.getSchedule(this.enrollmentId),
      saved: this.paymentsApi.getSavedMethods(),
      profile: this.profileApi.getProfile(),
    }).subscribe({
      next: ({ enrollment, schedule, saved, profile }) => {
        this.enrollment.set(enrollment);
        this.schedule.set(schedule);
        this.savedMethods.set(saved);
        this.profile.set(profile);
        this.prefill(profile);
        if (this.savedCards().length > 0) this.savedMethodId.set(this.savedCards()[0].paymentMethodId);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(getErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  // Autofill: name and billing address from what the customer gave at sign-up.
  private prefill(profile: UserProfile): void {
    const address = parseAddress(profile.address);
    this.form.patchValue({ name: profile.fullName, ...(address ?? {}) });
    this.addressFromProfile.set(address !== null);
  }

  selectMethod(type: PaymentMethodType): void {
    this.method.set(type);
    this.error.set(null);
    this.declined.set(null);
  }

  // ---- card input formatting

  onNumberInput(): void {
    const formatted = formatCardNumber(this.f.number.value);
    this.f.number.setValue(formatted, { emitEvent: false });
    this.brand.set(detectBrand(formatted));
    this.f.cvc.updateValueAndValidity();   // the code length depends on the brand
  }

  onExpiryInput(): void {
    this.f.expiry.setValue(formatExpiry(this.f.expiry.value), { emitEvent: false });
  }

  useTestCard(number: string): void {
    this.savedMethodId.set(null);
    this.f.number.setValue(number);
    this.onNumberInput();
    if (!this.f.expiry.value) this.f.expiry.setValue('12/30');
    if (!this.f.cvc.value) this.f.cvc.setValue(this.brand() === 'amex' ? '1234' : '123');
    this.f.cvc.updateValueAndValidity();
  }

  private cardNumberError(c: AbstractControl): ValidationErrors | null {
    const brand = detectBrand(c.value ?? '');
    const n = digitsOnly(c.value ?? '');
    if (!brand) return { brand: true };
    return n.length === cardLength(brand) && passesLuhn(n) ? null : { number: true };
  }

  private cvcError(c: AbstractControl): ValidationErrors | null {
    return digitsOnly(c.value ?? '').length === cvcLength(this.brand()) ? null : { cvc: true };
  }

  // ---- paying

  async payByCard(): Promise<void> {
    this.error.set(null);
    this.declined.set(null);

    const savedId = this.savedMethodId();
    if (savedId !== null) {
      this.submit({ savedPaymentMethodId: savedId });
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    this.processing.set('card');
    try {
      // The card goes to the payment provider, never to our API. We only send the token it returns.
      const tokenized = await this.provider.tokenizeCard({
        number: v.number,
        expiry: v.expiry,
        cvc: v.cvc,
        name: v.name,
        postalCode: v.postalCode,
      });
      this.submit({ paymentMethodToken: tokenized.token, saveMethod: v.saveCard });
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Your card could not be read.');
      this.processing.set('idle');
    }
  }

  openWallet(type: WalletType): void {
    this.error.set(null);
    this.declined.set(null);
    this.walletPrompt.set(type);
  }

  async approveWallet(): Promise<void> {
    const type = this.walletPrompt();
    if (!type) return;
    this.processing.set('wallet');
    const tokenized = await this.provider.approveWallet(type);
    this.walletPrompt.set(null);
    this.submit({ paymentMethodToken: tokenized.token });
  }

  private submit(method: Partial<Pick<CheckoutRequest, 'paymentMethodToken' | 'savedPaymentMethodId' | 'saveMethod'>>): void {
    const v = this.form.getRawValue();
    this.processing.set('charging');

    this.paymentsApi
      .pay(this.paymentId, {
        ...method,
        saveMethod: method.saveMethod ?? false,
        idempotencyKey: this.attemptKey,
        billingName: v.name || undefined,
        billingPostalCode: v.postalCode || undefined,
      })
      .subscribe({
        next: (result) => {
          this.attemptKey = newAttemptKey();   // a definite answer: the next click is a new attempt
          this.processing.set('idle');
          if (result.outcome === 'Succeeded') {
            this.router.navigate(['/receipts', result.transaction.transactionId]);
          } else {
            this.declined.set({ message: result.message, reference: result.transaction.reference });
          }
        },
        error: (err) => {
          this.processing.set('idle');
          this.error.set(getErrorMessage(err));
          // A 4xx is a definite answer. A lost connection or a 5xx is not: keeping the same key makes
          // "Pay" again a safe retry. If the first request did go through, the API replays its result.
          if (err.status >= 400 && err.status < 500) this.attemptKey = newAttemptKey();
        },
      });
  }

  removeSavedMethod(m: SavedPaymentMethod): void {
    this.paymentsApi.deleteSavedMethod(m.paymentMethodId).subscribe({
      next: () => {
        this.savedMethods.update((list) => list.filter((x) => x.paymentMethodId !== m.paymentMethodId));
        if (this.savedMethodId() === m.paymentMethodId) {
          this.savedMethodId.set(this.savedCards()[0]?.paymentMethodId ?? null);
        }
      },
      error: (err) => this.error.set(getErrorMessage(err)),
    });
  }

  methodLabel(type: PaymentMethodType): string {
    return METHOD_LABELS[type];
  }
}
