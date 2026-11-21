import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  HandSourceRequest,
  ScrapingSourceModel,
  ScrapingSourceStatus,
  SourceReview,
  SourceWithholdReport,
} from '../models/scraping-source.model';

@Injectable({ providedIn: 'root' })
export class ScrapingSourceService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/ScrapingSource`;

  getSources(status?: ScrapingSourceStatus): Observable<ScrapingSourceModel[]> {
    const params = status ? new HttpParams().set('status', status) : new HttpParams();
    return this.http.get<ScrapingSourceModel[]>(this.apiUrl, { params });
  }

  getReview(): Observable<SourceReview> {
    return this.http.get<SourceReview>(`${this.apiUrl}/review`);
  }

  reject(id: number, notes?: string): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/${id}/reject`, {
      notes: notes || undefined,
    });
  }

  approveCandidate(candidateId: number): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/candidates/${candidateId}/approve`, {});
  }

  pauseHarvest(id: number, reason?: string): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/${id}/pause-harvest`, { reason: reason || undefined });
  }

  resumeHarvest(id: number): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/${id}/resume-harvest`, {});
  }

  withholdRecipes(id: number, apply: boolean, reason?: string): Observable<SourceWithholdReport> {
    const params = new HttpParams().set('apply', apply);
    return this.http.post<SourceWithholdReport>(`${this.apiUrl}/${id}/withhold-recipes`, { reason: reason || undefined }, { params });
  }

  releaseRecipes(id: number, apply: boolean): Observable<SourceWithholdReport> {
    const params = new HttpParams().set('apply', apply);
    return this.http.post<SourceWithholdReport>(`${this.apiUrl}/${id}/release-recipes`, {}, { params });
  }

  recordHand(request: HandSourceRequest): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/hand`, request);
  }

  undo(entryId: number): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/journal/${entryId}/undo`, {});
  }
}
