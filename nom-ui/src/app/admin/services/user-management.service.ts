// File: nom-ui/src/app/admin/services/user-management.service.ts

import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { UpdateUserClaimsRequestModel } from '../models/update-user-claims-request.model';

@Injectable({
  providedIn: 'root'
})
export class UserManagementService {
  private http = inject(HttpClient);

  private readonly apiUrl = `api/UserManagement`;



  updateUserClaims(request: UpdateUserClaimsRequestModel): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/claims`, request);
  }
}