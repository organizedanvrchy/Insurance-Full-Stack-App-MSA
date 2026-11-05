import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_URL } from '../api.config';
import { Customer, CustomerDetailsUpdate } from '../models/customer/customer';

@Injectable({ providedIn: 'root' })
export class CustomerService {
  private http = inject(HttpClient);
  private url = `${API_URL}/admin/customers`;

  // Both filters are optional: omit them to get everyone.
  getAll(search?: string, isActive?: boolean): Observable<Customer[]> {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (isActive !== undefined) params = params.set('isActive', isActive);
    return this.http.get<Customer[]>(this.url, { params });
  }

  get(id: number): Observable<Customer> {
    return this.http.get<Customer>(`${this.url}/${id}`);
  }

  setActive(id: number, isActive: boolean): Observable<Customer> {
    return this.http.put<Customer>(`${this.url}/${id}/active`, { isActive });
  }

  /** A new email must be verified again by the customer; they're told an administrator changed it. */
  update(id: number, details: CustomerDetailsUpdate): Observable<Customer> {
    return this.http.put<Customer>(`${this.url}/${id}`, details);
  }

  /** Emails the customer a 30-minute one-time password; their old password stops working at once. */
  resetPassword(id: number): Observable<Customer> {
    return this.http.post<Customer>(`${this.url}/${id}/reset-password`, {});
  }
}
