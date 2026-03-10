import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RetailPackagingResponse } from '../models/retail-packaging-response.model';
import { RetailPackagingCreateRequest } from '../models/retail-packaging-create-request.model';
import { RetailPackagingLookupRequest } from '../models/retail-packaging-lookup-request.model';
import { RetailPackagingLookupResponse } from '../models/retail-packaging-lookup-response.model';

@Injectable({ providedIn: 'root' })
export class RetailPackagingService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/RetailPackaging`;

  getAll(): Observable<RetailPackagingResponse[]> {
    return this.http.get<RetailPackagingResponse[]>(this.apiUrl);
  }

  getById(id: number): Observable<RetailPackagingResponse> {
    return this.http.get<RetailPackagingResponse>(`${this.apiUrl}/${id}`);
  }

  create(model: RetailPackagingCreateRequest): Observable<RetailPackagingResponse> {
    return this.http.post<RetailPackagingResponse>(this.apiUrl, model);
  }

  lookup(ingredientNames: string[]): Observable<RetailPackagingLookupResponse> {
    return this.http.post<RetailPackagingLookupResponse>(
      `${this.apiUrl}/lookup`,
      { ingredientNames } as RetailPackagingLookupRequest
    );
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
