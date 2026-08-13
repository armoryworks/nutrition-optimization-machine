import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MealSplit, PortionBreakdown, RangeCookFactor } from '../models/portion.model';

@Injectable({ providedIn: 'root' })
export class PortionService {
  private http = inject(HttpClient);
  private readonly mealPlanUrl = `${environment.apiUrl}/MealPlan`;
  private readonly householdUrl = `${environment.apiUrl}/Household`;

  getPortions(householdId: number, date: string, mealTypeId: number): Observable<PortionBreakdown> {
    const params = new HttpParams()
      .set('householdId', householdId)
      .set('date', date)
      .set('mealTypeId', mealTypeId);
    return this.http.get<PortionBreakdown>(`${this.mealPlanUrl}/portions`, { params });
  }

  getRangeCookFactors(householdId: number, startDate: string, endDate: string): Observable<RangeCookFactor[]> {
    const params = new HttpParams()
      .set('householdId', householdId)
      .set('startDate', startDate)
      .set('endDate', endDate);
    return this.http.get<RangeCookFactor[]>(`${this.mealPlanUrl}/portions/range`, { params });
  }

  getMealSplit(householdId: number): Observable<MealSplit> {
    return this.http.get<MealSplit>(`${this.householdUrl}/${householdId}/meal-split`);
  }

  saveMealSplit(householdId: number, split: MealSplit): Observable<MealSplit> {
    return this.http.put<MealSplit>(`${this.householdUrl}/${householdId}/meal-split`, split);
  }
}
