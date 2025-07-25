// File: nom-ui/src/app/curation/services/curation.service.ts

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SubmitForCurationRequestModel } from '../models/submit-for-curation-request.model';
import { CurationDecisionRequestModel } from '../models/curation-decision-request.model';

@Injectable({
  providedIn: 'root'
})
export class CurationService {
  private readonly apiUrl = `api/Curation`;

  constructor(private http: HttpClient) { }

  submitForCuration(request: SubmitForCurationRequestModel): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/submit`, request);
  }

  approve(request: CurationDecisionRequestModel): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/approve`, request);
  }

  requestRevision(request: CurationDecisionRequestModel): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/request-revision`, request);
  }

  reject(request: CurationDecisionRequestModel): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/reject`, request);
  }
}