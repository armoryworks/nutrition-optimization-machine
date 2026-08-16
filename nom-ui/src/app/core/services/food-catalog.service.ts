import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  FoodCatalogAuditResult,
  FoodCatalogPage,
  FoodCatalogItem,
  FoodCatalogUpdate,
  FoodProposal,
} from '../models/food-catalog.model';

/** Admin review of the imported food catalog, its quality audit, and reviewer proposals. */
@Injectable({ providedIn: 'root' })
export class FoodCatalogService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/FoodCatalog`;

  getPage(filters: {
    source?: string | null;
    status?: number | null;
    foodGroupId?: number | null;
    search?: string | null;
    page?: number;
    pageSize?: number;
  }): Observable<FoodCatalogPage> {
    let params = new HttpParams();
    if (filters.source) params = params.set('source', filters.source);
    if (filters.status != null) params = params.set('status', filters.status);
    if (filters.foodGroupId != null) params = params.set('foodGroupId', filters.foodGroupId);
    if (filters.search) params = params.set('search', filters.search);
    params = params.set('page', filters.page ?? 1).set('pageSize', filters.pageSize ?? 50);
    return this.http.get<FoodCatalogPage>(this.apiUrl, { params });
  }

  audit(source?: string | null, limit = 5000): Observable<FoodCatalogAuditResult> {
    let params = new HttpParams().set('limit', limit);
    if (source) params = params.set('source', source);
    return this.http.get<FoodCatalogAuditResult>(`${this.apiUrl}/audit`, { params });
  }

  update(id: number, update: FoodCatalogUpdate): Observable<FoodCatalogItem> {
    return this.http.put<FoodCatalogItem>(`${this.apiUrl}/${id}`, update);
  }

  setCurationStatus(ingredientIds: number[], curationStatusId: number): Observable<{ updated: number }> {
    return this.http.post<{ updated: number }>(`${this.apiUrl}/curation-status`, {
      ingredientIds,
      curationStatusId,
    });
  }

  /** Download URL for the reviewer CSV export. */
  exportUrl(source?: string | null, status?: number | null, limit = 5000): string {
    const params = new URLSearchParams({ limit: String(limit) });
    if (source) params.set('source', source);
    if (status != null) params.set('status', String(status));
    return `${this.apiUrl}/export?${params.toString()}`;
  }

  getProposals(batch?: string | null, status = 'Pending', limit = 200): Observable<FoodProposal[]> {
    let params = new HttpParams().set('status', status).set('limit', limit);
    if (batch) params = params.set('batch', batch);
    return this.http.get<FoodProposal[]>(`${this.apiUrl}/proposals`, { params });
  }

  ingestProposals(csv: string, batch?: string): Observable<{ batch: string; accepted: number; rejected: number; rejectedByReason: Record<string, number> }> {
    return this.http.post<{ batch: string; accepted: number; rejected: number; rejectedByReason: Record<string, number> }>(
      `${this.apiUrl}/proposals`, { csv, batch });
  }

  applyProposal(id: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/proposals/${id}/apply`, {});
  }

  rejectProposal(id: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/proposals/${id}/reject`, {});
  }
}
