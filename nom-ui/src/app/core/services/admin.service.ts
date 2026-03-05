import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface CurationQueueItem {
  id: number;
  entityId: number;
  entityType: string;
  entityName: string;
  authorName: string;
  submittedDate: string;
  status: string;
  feedbackNotes: string | null;
}

export interface CurationDecisionRequest {
  entityId: number;
  entityType: string;
  feedbackNotes: string;
}

export interface UserSummary {
  userId: string;
  email: string;
  fullName: string;
  isAdmin: boolean;
  isCurator: boolean;
  emailConfirmed: boolean;
  twoFactorEnabled: boolean;
  createdDate: string;
}

export interface UpdateUserClaimsRequest {
  userId: string;
  claims: { type: string; value: string }[];
}

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
