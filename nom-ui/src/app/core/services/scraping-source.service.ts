import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  HandSourceRequest,
  RightsStep,
  ScrapingSourceModel,
  ScrapingSourceStatus,
  SourcePermissionRequest,
  SourceRecipeStep,
  SourceRefusingHost,
  SourceReview,
  SourceRightsByHandRequest,
  SourceRightsByHandResult,
  SourceRightsRun,
  SourceTaken,
  RecipeRemovalReport,
  RecipeRemovalStatus,
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

  termsCheck(id: number, reason?: string): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/${id}/terms-check`, { reason: reason || undefined });
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

  recordRightsByHand(request: SourceRightsByHandRequest, apply: boolean): Observable<SourceRightsByHandResult> {
    return this.http.post<SourceRightsByHandResult>(`${this.apiUrl}/rights/by-hand`, request, { params: new HttpParams().set('apply', apply) });
  }

  releaseRefusing(id: number, reason?: string): Observable<SourceRefusingHost> {
    return this.http.post<SourceRefusingHost>(`${this.apiUrl}/refusing/${id}/release`, { reason: reason || undefined });
  }

  undo(entryId: number): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/journal/${entryId}/undo`, {});
  }

  deriveRights(apply: boolean): Observable<SourceRightsRun> {
    return this.http.post<SourceRightsRun>(`${this.apiUrl}/rights/derive`, {}, { params: new HttpParams().set('apply', apply) });
  }

  recordPermission(id: number, request: SourcePermissionRequest): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/${id}/permission`, request);
  }

  factsOnlyUse(id: number, approve: boolean): Observable<ScrapingSourceModel> {
    return this.http.post<ScrapingSourceModel>(`${this.apiUrl}/${id}/facts-only-use`, {}, { params: new HttpParams().set('approve', approve) });
  }

  rightsStep(step: RightsStep, apply: boolean): Observable<SourceRecipeStep> {
    return this.http.post<SourceRecipeStep>(`${this.apiUrl}/rights/${step}`, {}, { params: new HttpParams().set('apply', apply) });
  }

  undoRightsStep(step: RightsStep, stamp: string, apply: boolean): Observable<SourceRecipeStep> {
    const params = new HttpParams().set('step', step).set('stamp', stamp).set('apply', apply);
    return this.http.post<SourceRecipeStep>(`${this.apiUrl}/rights/undo`, {}, { params });
  }

  taken(): Observable<SourceTaken[]> {
    return this.http.get<SourceTaken[]>(`${this.apiUrl}/rights/taken`);
  }

  removalStatus(): Observable<RecipeRemovalStatus> {
    return this.http.get<RecipeRemovalStatus>(`${this.apiUrl}/rights/removal`);
  }

  retireRecipes(sourceId: number | null, apply: boolean, reason?: string): Observable<RecipeRemovalReport> {
    let params = new HttpParams().set('apply', apply);
    if (sourceId != null) params = params.set('sourceId', sourceId);
    return this.http.post<RecipeRemovalReport>(`${this.apiUrl}/rights/removal/retire`, { reason: reason ?? null }, { params });
  }

  undoRetire(stamp: string, apply: boolean): Observable<RecipeRemovalReport> {
    const params = new HttpParams().set('stamp', stamp).set('apply', apply);
    return this.http.post<RecipeRemovalReport>(`${this.apiUrl}/rights/removal/undo`, {}, { params });
  }

  purgeRecipes(sourceId: number | null, apply: boolean, backupTaken: boolean): Observable<RecipeRemovalReport> {
    let params = new HttpParams().set('apply', apply).set('backupTaken', backupTaken);
    if (sourceId != null) params = params.set('sourceId', sourceId);
    return this.http.post<RecipeRemovalReport>(`${this.apiUrl}/rights/removal/purge`, {}, { params });
  }
}
