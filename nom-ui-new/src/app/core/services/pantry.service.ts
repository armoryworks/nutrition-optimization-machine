import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  PantryItemResponse,
  PantryItemCreateRequest,
  PantryItemUpdateRequest,
  ShoppingNeedsResponse,
} from '../models/pantry.model';

@Injectable({ providedIn: 'root' })
export class PantryService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Pantry`;

  getPantryItems(householdId: number): Observable<PantryItemResponse[]> {
    const params = new HttpParams().set('householdId', householdId);
    return this.http.get<PantryItemResponse[]>(this.apiUrl, { params });
  }

  getPantryItem(id: number): Observable<PantryItemResponse> {
    return this.http.get<PantryItemResponse>(`${this.apiUrl}/${id}`);
  }

  addPantryItem(request: PantryItemCreateRequest): Observable<PantryItemResponse> {
    return this.http.post<PantryItemResponse>(this.apiUrl, request);
  }

  updatePantryItem(id: number, request: PantryItemUpdateRequest): Observable<PantryItemResponse> {
    return this.http.put<PantryItemResponse>(`${this.apiUrl}/${id}`, request);
  }

  removePantryItem(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  getShoppingNeeds(householdId: number, daysAhead: number): Observable<ShoppingNeedsResponse> {
    const params = new HttpParams()
      .set('householdId', householdId)
      .set('daysAhead', daysAhead);
    return this.http.get<ShoppingNeedsResponse>(`${this.apiUrl}/shopping-needs`, { params });
  }
}
