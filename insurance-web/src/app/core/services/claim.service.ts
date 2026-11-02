import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_URL } from '../api.config';
import { ClaimStatus } from '../models/enums';
import { Claim } from '../models/claim/claim';
import { ClaimDocument } from '../models/claim/claimDocument';
import { ClaimReviewRequest } from '../models/claim/claimReviewRequest';
import { ClaimSubmitRequest } from '../models/claim/claimSubmitRequest';

@Injectable({ providedIn: 'root' })
export class ClaimService {
  private http = inject(HttpClient);
  private url = `${API_URL}/claims`;

  submit(req: ClaimSubmitRequest): Observable<Claim> {
    return this.http.post<Claim>(this.url, req);
  }

  getMine(): Observable<Claim[]> {
    return this.http.get<Claim[]>(`${this.url}/mine`);
  }

  // Admin only. Omit `status` for all claims.
  getAll(status?: ClaimStatus): Observable<Claim[]> {
    return this.http.get<Claim[]>(this.url, { params: status ? { status } : {} });
  }

  review(id: number, req: ClaimReviewRequest): Observable<Claim> {
    return this.http.put<Claim>(`${this.url}/${id}/review`, req);
  }

  // Customer: back to the reviewer after a request for more information.
  sendToReviewer(id: number): Observable<Claim> {
    return this.http.post<Claim>(`${this.url}/${id}/send-to-reviewer`, null);
  }

  getDocuments(claimId: number): Observable<ClaimDocument[]> {
    return this.http.get<ClaimDocument[]>(`${this.url}/${claimId}/documents`);
  }

  // One file per request. FormData sets its own Content-Type, including the multipart boundary.
  uploadDocument(claimId: number, file: File): Observable<ClaimDocument> {
    const body = new FormData();
    body.append('file', file);
    return this.http.post<ClaimDocument>(`${this.url}/${claimId}/documents`, body);
  }

  // Through HttpClient, not a plain link, so the request carries the JWT.
  downloadDocument(claimId: number, documentId: number): Observable<Blob> {
    return this.http.get(`${this.url}/${claimId}/documents/${documentId}`, { responseType: 'blob' });
  }
}
