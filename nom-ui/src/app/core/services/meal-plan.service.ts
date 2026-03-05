import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  MealPlanWeekResponse,
  MealPlanCreateRequest,
  MealPlanUpdateRequest,
  MealPlanResponse,
  MealPlanExclusion,
  MealPlanExclusionCreateRequest,
  MealPlanShuffleRequest,
  MealPlanShuffleResponse,
  MealPlanRule,
  MealPlanRuleCreateRequest,
} from '../models/meal-plan.model';

@Injectable({ providedIn: 'root' })
export class MealPlanService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/MealPlan`;

  getWeek(householdId: number, weekStart: string): Observable<MealPlanWeekResponse> {
    const params = new HttpParams()
      .set('householdId', householdId)
      .set('weekStart', weekStart);
    return this.http.get<MealPlanWeekResponse>(`${this.apiUrl}/week`, { params });
  }

  createMealPlan(request: MealPlanCreateRequest): Observable<MealPlanResponse> {
    return this.http.post<MealPlanResponse>(this.apiUrl, request);
  }

  updateMealPlan(id: number, request: MealPlanUpdateRequest): Observable<MealPlanResponse> {
    return this.http.put<MealPlanResponse>(`${this.apiUrl}/${id}`, request);
  }

  deleteMealPlan(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  createExclusion(request: MealPlanExclusionCreateRequest): Observable<MealPlanExclusion> {
    return this.http.post<MealPlanExclusion>(`${this.apiUrl}/exclusion`, request);
  }

  getExclusions(householdId: number, startDate: string, endDate: string): Observable<MealPlanExclusion[]> {
    const params = new HttpParams()
      .set('householdId', householdId)
      .set('startDate', startDate)
      .set('endDate', endDate);
    return this.http.get<MealPlanExclusion[]>(`${this.apiUrl}/exclusion`, { params });
  }

  deleteExclusion(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/exclusion/${id}`);
  }

  shuffle(request: MealPlanShuffleRequest): Observable<MealPlanShuffleResponse> {
    return this.http.post<MealPlanShuffleResponse>(`${this.apiUrl}/shuffle`, request);
  }

  completeMealPlan(id: number): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/${id}/complete`, {});
  }

  getRules(householdId: number): Observable<MealPlanRule[]> {
    return this.http.get<MealPlanRule[]>(`${this.apiUrl}/rule`, { params: { householdId } });
  }

  createRule(request: MealPlanRuleCreateRequest): Observable<MealPlanRule> {
    return this.http.post<MealPlanRule>(`${this.apiUrl}/rule`, request);
  }

  deleteRule(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/rule/${id}`);
  }
}
