import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiToken, CreateApiTokenRequest, CreatedApiToken } from '../models/api-token.model';

@Injectable({ providedIn: 'root' })
export class ApiTokenService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/User/api-tokens`;

  getTokens(): Observable<ApiToken[]> {
    return this.http.get<ApiToken[]>(this.apiUrl);
  }

  createToken(request: CreateApiTokenRequest): Observable<CreatedApiToken> {
    return this.http.post<CreatedApiToken>(this.apiUrl, request);
  }

  revokeToken(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${encodeURIComponent(id)}`);
  }
}
