import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LinkUndoPreview, PipelineGate, PipelineStatus, RereadPreview, UnfoldPreview } from '../models/pipeline.model';

/** Admin view of the automated catalog pipeline, the model lanes' pause switches, and the dry runs of the line operations. */
@Injectable({ providedIn: 'root' })
export class PipelineService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Pipeline`;
  private readonly catalogUrl = `${environment.apiUrl}/FoodCatalog`;

  getStatus(refresh = false): Observable<PipelineStatus> {
    const params = refresh ? new HttpParams().set('refresh', true) : undefined;
    return this.http.get<PipelineStatus>(`${this.apiUrl}/status`, { params });
  }

  /** Turns the pause switch of one model lane, or of every lane with 'all'. */
  setPause(lane: string, paused: boolean): Observable<PipelineGate> {
    return this.http.put<PipelineGate>(`${this.apiUrl}/pause/${encodeURIComponent(lane)}`, { isEnabled: paused });
  }

  /** What the unfold of joined rows would do; always a dry run. */
  previewUnfold(): Observable<UnfoldPreview> {
    const params = new HttpParams().set('apply', false).set('maxRows', 50000).set('sampleSize', 0);
    return this.http.post<UnfoldPreview>(`${this.catalogUrl}/relink/unfold`, null, { params });
  }

  /** What the container re-read would change; no writes. */
  previewReread(): Observable<RereadPreview> {
    const params = new HttpParams().set('sampleSize', 0);
    return this.http.get<RereadPreview>(`${this.catalogUrl}/reread/preview`, { params });
  }

  /** What taking back the links applied since a time would do; always a dry run. */
  previewLinkUndo(sinceUtc: string, leaveMovedLines: boolean): Observable<LinkUndoPreview> {
    const params = new HttpParams()
      .set('sinceUtc', sinceUtc)
      .set('apply', false)
      .set('max', 5000)
      .set('sampleSize', 0)
      .set('leaveMovedLines', leaveMovedLines);
    return this.http.post<LinkUndoPreview>(`${this.catalogUrl}/proposals/undo-applied`, null, { params });
  }
}
