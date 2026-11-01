// Keep in sync with Api/Enums/*.cs and the SQL CHECK constraints.
export const USER_ROLES = ['Customer', 'Admin'] as const;
export type UserRole = (typeof USER_ROLES)[number];

export const POLICY_TYPES = ['Health', 'Auto', 'Life', 'Home', 'Travel'] as const;
export type PolicyType = (typeof POLICY_TYPES)[number];

export const ENROLLMENT_STATUSES = ['Active', 'Cancelled', 'Expired', 'Pending', 'Failed'] as const;
export type EnrollmentStatus = (typeof ENROLLMENT_STATUSES)[number];

export const PAYMENT_STATUSES = ['Pending', 'Paid', 'Overdue'] as const;
export type PaymentStatus = (typeof PAYMENT_STATUSES)[number];

// Simulated payments. Keep in sync with Billing's PaymentMethodType, TransactionStatus and CheckoutOutcome.
export const PAYMENT_METHOD_TYPES = ['Card', 'PayPal', 'ApplePay', 'GooglePay', 'Venmo'] as const;
export type PaymentMethodType = (typeof PAYMENT_METHOD_TYPES)[number];

export const TRANSACTION_STATUSES = ['Processing', 'Succeeded', 'Declined', 'Failed', 'Refunded'] as const;
export type TransactionStatus = (typeof TRANSACTION_STATUSES)[number];

export type CheckoutOutcome = 'Succeeded' | 'Declined';

export const CLAIM_STATUSES = ['Submitted', 'UnderReview', 'Approved', 'Rejected', 'Paid'] as const;
export type ClaimStatus = (typeof CLAIM_STATUSES)[number];

// Computed by the API (not stored in the database):
// what a customer needs to do about premiums.
export const PAYMENT_ALERTS = ['None', 'DueSoon', 'Overdue'] as const;
export type PaymentAlert = (typeof PAYMENT_ALERTS)[number];

// Quote Enums
export type QuestionType = 'Number' | 'Boolean' | 'Date' | 'Choice';
export type AnswerValue = string | number | boolean;
