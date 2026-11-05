import { CurrencyPipe, DatePipe, DecimalPipe } from "@angular/common";
import { Component, inject, signal } from "@angular/core";
import { FormControl, FormGroup, ReactiveFormsModule, ValidatorFn, Validators } from "@angular/forms";
import { ActivatedRoute, Router, RouterLink } from "@angular/router";
import { PolicyService } from "../../core/services/policy.service";
import { EnrollmentService } from "../../core/services/enrollment.service";
import { Policy } from "../../core/models/policy/policy";
import { getErrorMessage } from "../../core/utils/http-error";
import { QuoteService } from "../../core/services/quote.service";
import { Question } from "../../core/models/quote/Question";
import { Quote } from "../../core/models/quote/Quote";
import { forkJoin } from "rxjs";
import { AnswerValue } from "../../core/models/enums";
import { AuthService } from "../../core/services/auth.service";

@Component({
  selector: 'app-enroll',
  imports: [ReactiveFormsModule, RouterLink, CurrencyPipe, DatePipe, DecimalPipe],
  templateUrl: "enroll.html",
  styleUrl: "enroll.component.css",
})
export class EnrollComponent {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private policies = inject(PolicyService);
  private quotes = inject(QuoteService);
  private enrollments = inject(EnrollmentService);
  auth = inject(AuthService);

  // Local calendar date as YYYY-MM-DD (toISOString would give the UTC date, which can be "tomorrow" in the evening).
  readonly today = new Date().toLocaleDateString('en-CA');
  startDate = new FormControl(this.today, { nonNullable: true, validators: [Validators.required] });

  policy = signal<Policy | null>(null);
  questions = signal<Question[]>([]);
  form = signal<FormGroup>(new FormGroup({}));
  quote = signal<Quote | null>(null);
  saving = signal(false);
  error = signal<string | null>(null);

  constructor() {
    const id = Number(this.route.snapshot.paramMap.get('id'));

    forkJoin({
      policy: this.policies.getById(id),
      questions: this.quotes.getQuestions(id),
    }).subscribe({
      next: ({ policy, questions }) => {
        this.form.set(this.buildForm(questions));   // build the form BEFORE the template needs it
        this.questions.set(questions);
        this.policy.set(policy);
      },
      error: (err) => this.error.set(getErrorMessage(err)),
    });
  }

  invalid(key: string): boolean {
    const c = this.form().get(key);
    return !!c && c.touched && c.invalid;
  }

  getQuote(p: Policy): void {
    if (this.form().invalid) {
      this.form().markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);

    const answers: Record<string, AnswerValue> = {};
    for (const q of this.questions()) {
      answers[q.key] = this.form().get(q.key)!.value as AnswerValue;
    }

    this.quotes.create({ policyId: p.policyId, answers }).subscribe({
      next: (qt) => {
        this.quote.set(qt);
        this.saving.set(false);
      },
      error: (err) => {
        this.error.set(getErrorMessage(err));   // 400: invalid answer, or "unable to offer this coverage"
        this.saving.set(false);
      },
    });
  }

  editAnswers(): void {
    this.quote.set(null);
    this.error.set(null);
  }

  enroll(qt: Quote): void {
    if (this.startDate.invalid) {
      this.startDate.markAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);

    this.enrollments.enroll({ quoteId: qt.quoteId, startDate: this.startDate.value }).subscribe({
      next: () => this.router.navigate(['/my-policies']),
      error: (err) => {
        this.error.set(getErrorMessage(err));   // 409 duplicate, 400 expired quote or bad date
        this.saving.set(false);
      },
    });
  }

  // One control per question, with validators taken from the question's own rules.
  private buildForm(questions: Question[]): FormGroup {
    const controls: Record<string, FormControl<AnswerValue | null>> = {};

    for (const q of questions) {
      const validators: ValidatorFn[] = [];
      let initial: AnswerValue | null = null;

      switch (q.kind) {
        case 'Number':
          validators.push(Validators.required);
          if (q.min !== null) validators.push(Validators.min(q.min));
          if (q.max !== null) validators.push(Validators.max(q.max));
          break;
        case 'Boolean':
          initial = false;           // an unticked box is a valid "No"
          break;
        case 'Date':
        case 'Choice':
          initial = '';
          validators.push(Validators.required);
          break;
      }

      controls[q.key] = new FormControl<AnswerValue | null>(initial, validators);
    }

    return new FormGroup(controls);
  }
}
