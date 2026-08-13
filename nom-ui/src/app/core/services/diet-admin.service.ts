import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  RestrictionGroupModel,
  RestrictionCategoryModel,
  RestrictionCriterionModel,
  SaveRestrictionCriterionRequest,
} from '../models/diet-admin.model';

@Injectable({ providedIn: 'root' })
export class DietAdminService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/DietAdmin`;

  getGroups(): Observable<RestrictionGroupModel[]> {
    return this.http.get<RestrictionGroupModel[]>(`${this.apiUrl}/groups`);
  }

  createCategory(groupId: number, name: string, description?: string): Observable<RestrictionCategoryModel> {
    return this.http.post<RestrictionCategoryModel>(`${this.apiUrl}/categories`, { groupId, name, description });
  }

  updateCategory(id: number, name: string, description?: string): Observable<RestrictionCategoryModel> {
    return this.http.put<RestrictionCategoryModel>(`${this.apiUrl}/categories/${id}`, { name, description });
  }

  deleteCategory(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/categories/${id}`);
  }

  getCriteria(categoryId: number): Observable<RestrictionCriterionModel[]> {
    return this.http.get<RestrictionCriterionModel[]>(`${this.apiUrl}/categories/${categoryId}/criteria`);
  }

  addCriterion(categoryId: number, request: SaveRestrictionCriterionRequest): Observable<RestrictionCriterionModel> {
    return this.http.post<RestrictionCriterionModel>(`${this.apiUrl}/categories/${categoryId}/criteria`, request);
  }

  deleteCriterion(criterionId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/criteria/${criterionId}`);
  }
}
