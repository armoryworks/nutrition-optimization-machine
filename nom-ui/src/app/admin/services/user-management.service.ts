// File: nom-ui/src/app/admin/services/user-management.service.ts

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { UpdateUserClaimsRequestModel } from '../models/update-user-claims-request.model';

@Injectable({
  providedIn: 'root'
})
export class UserManagementService {
  private readonly apiUrl = `api/UserManagement`;

  constructor(private http: HttpClient) { }

  updateUserClaims(request: UpdateUserClaimsRequestModel): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/claims`, request);
  }
}