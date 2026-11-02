import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_URL } from '../api.config';
import { NotificationPreferences, SentEmail } from '../models/notification/notification';

/** The customer's email settings and the emails we've sent them (Notifications service). */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private http = inject(HttpClient);
  private url = `${API_URL}/notifications`;

  getPreferences(): Observable<NotificationPreferences> {
    return this.http.get<NotificationPreferences>(`${this.url}/preferences`);
  }

  updatePreferences(prefs: NotificationPreferences): Observable<NotificationPreferences> {
    return this.http.put<NotificationPreferences>(`${this.url}/preferences`, prefs);
  }

  getHistory(take = 10): Observable<SentEmail[]> {
    return this.http.get<SentEmail[]>(`${this.url}/history`, { params: new HttpParams().set('take', take) });
  }
}
