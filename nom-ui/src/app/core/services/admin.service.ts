import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CurationQueueItem } from '../models/curation-queue-item.model';
import { CurationDecisionRequest } from '../models/curation-decision-request.model';
import { UpdateUserClaimsRequest } from '../models/update-user-claims-request.model';

@Injectable({ providedIn: 'root' })
export class AdminService {
  private http = inject(HttpClient);

  // Curation
  getCurationQueue(): Observable<CurationQueueItem[]> {
    return this.http.get<CurationQueueItem[]>(`${environment.apiUrl}/Curation/queue`);
  }

  approveCuration(request: CurationDecisionRequest): Observable<void> {
    return this.http.post<void>(`${environment.apiUrl}/Curation/approve`, request);
  }

  requestRevision(request: CurationDecisionRequest): Observable<void> {
    return this.http.post<void>(`${environment.apiUrl}/Curation/request-revision`, request);
  }

  rejectCuration(request: CurationDecisionRequest): Observable<void> {
    return this.http.post<void>(`${environment.apiUrl}/Curation/reject`, request);
  }

  // User Management
  updateUserClaims(request: UpdateUserClaimsRequest): Observable<void> {
    return this.http.put<void>(`${environment.apiUrl}/UserManagement/claims`, request);
  }
}
