import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_URL } from '../api.config';
import { TransactionStatus } from '../models/enums';
import { CheckoutRequest, CheckoutResult, SavedPaymentMethod, Transaction } from '../models/payment/checkout';
import { Payment } from '../models/payment/payment';

@Injectable({ providedIn: 'root' })
export class PaymentService {
  private http = inject(HttpClient);

  getSchedule(enrollmentId: number): Observable<Payment[]> {
    return this.http.get<Payment[]>(`${API_URL}/enrollments/${enrollmentId}/payments`);
  }

  /** Pays one installment with a token from the payment provider (or a saved method). */
  pay(paymentId: number, request: CheckoutRequest): Observable<CheckoutResult> {
    return this.http.post<CheckoutResult>(`${API_URL}/payments/${paymentId}/pay`, request);
  }

  /** The payment log: one policy, or (no enrollmentId) the customer's own attempts / everyone's for an admin. */
  getTransactions(filter: { enrollmentId?: number; status?: TransactionStatus } = {}): Observable<Transaction[]> {
    let params = new HttpParams();
    if (filter.enrollmentId) params = params.set('enrollmentId', filter.enrollmentId);
    if (filter.status) params = params.set('status', filter.status);
    return this.http.get<Transaction[]>(`${API_URL}/payments/transactions`, { params });
  }

  getTransaction(transactionId: number): Observable<Transaction> {
    return this.http.get<Transaction>(`${API_URL}/payments/transactions/${transactionId}`);
  }

  getSavedMethods(): Observable<SavedPaymentMethod[]> {
    return this.http.get<SavedPaymentMethod[]>(`${API_URL}/payment-methods`);
  }

  deleteSavedMethod(paymentMethodId: number): Observable<void> {
    return this.http.delete<void>(`${API_URL}/payment-methods/${paymentMethodId}`);
  }
}
