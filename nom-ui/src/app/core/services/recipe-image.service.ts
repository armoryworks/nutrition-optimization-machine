import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeImageCandidate, RecipeImageCandidateStatus, RecipeImageCheckSummary } from '../models/recipe-image.model';

const CHECK_NAMES = ['metadata', 'caption', 'vqa', 'ranking'] as const;

/**
 * Admin access to the recipe image review queue fed by ops/recipe-image-finder.py.
 */
@Injectable({ providedIn: 'root' })
export class RecipeImageService {
  private http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/RecipeImages`;

  getCandidates(status: RecipeImageCandidateStatus): Observable<RecipeImageCandidate[]> {
    return this.http.get<RecipeImageCandidate[]>(`${this.baseUrl}/candidates`, { params: { status } });
  }

  getCandidateImage(id: number): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/candidates/${id}/image`, { responseType: 'blob' });
  }

  approve(id: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/candidates/${id}/approve`, {});
  }

  reject(id: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/candidates/${id}/reject`, {});
  }

  remove(id: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/candidates/${id}/remove`, {});
  }
}

/** The four triangulation checks from the finder's evidence, in a fixed order. */
export function summarizeImageChecks(checksJson?: string | null): RecipeImageCheckSummary[] {
  if (!checksJson) return [];
  let evidence: Record<string, unknown>;
  try {
    evidence = JSON.parse(checksJson) as Record<string, unknown>;
  } catch {
    return [];
  }
  if (!evidence || typeof evidence !== 'object') return [];
  return CHECK_NAMES.filter((name) => evidence[name] && typeof evidence[name] === 'object').map((name) => {
    const check = evidence[name] as { passed?: unknown; reasons?: unknown };
    return {
      name,
      passed: check.passed === true,
      reasons: Array.isArray(check.reasons) ? check.reasons.filter((r): r is string => typeof r === 'string') : [],
    };
  });
}
