import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ConsentRequest } from '../models/consent-request.model';
import { DataExportRequest } from '../models/data-export-request.model';
import { DataDeletionRequest } from '../models/data-deletion-request.model';

@Injectable({ providedIn: 'root' })
export class PrivacyService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Privacy`;

  updateConsent(request: ConsentRequest): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/consent`, request);
  }

  requestDataExport(request: DataExportRequest): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/data-export`, request);
  }

  requestDataDeletion(request: DataDeletionRequest): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/data-deletion`, request);
  }
}
