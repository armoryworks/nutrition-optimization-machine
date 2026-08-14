import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DishGroupModel, DishGroupDetailModel } from '../models/dish-group.model';

@Injectable({ providedIn: 'root' })
export class DishGroupService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/DishGroup`;

  /** All dish groups with visible-member counts, largest first. */
  list(limit = 200): Observable<DishGroupModel[]> {
    const params = new HttpParams().set('limit', limit);
    return this.http.get<DishGroupModel[]>(this.apiUrl, { params });
  }

  /** One group + the member recipes visible to the caller. */
  getBySlug(slug: string): Observable<DishGroupDetailModel> {
    return this.http.get<DishGroupDetailModel>(`${this.apiUrl}/${encodeURIComponent(slug)}`);
  }

  /** Merge one group's recipes into another and retire the source (curation admins). */
  merge(sourceId: number, targetId: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${sourceId}/merge-into/${targetId}`, {});
  }

  /**
   * Reassign a recipe's dish group (curation admins): by id, by name
   * (creating the group when new), or clear with both null.
   */
  assignRecipe(
    recipeId: number,
    assignment: { dishGroupId?: number | null; dishGroupName?: string | null },
  ): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/recipe/${recipeId}`, {
      dishGroupId: assignment.dishGroupId ?? null,
      dishGroupName: assignment.dishGroupName ?? null,
    });
  }
}
