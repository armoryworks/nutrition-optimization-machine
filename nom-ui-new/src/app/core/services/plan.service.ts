import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PlanModel, CreatePlanRequest, UpdatePlanRequest, ClonePlanRequest } from '../models/plan.model';

@Injectable({ providedIn: 'root' })
export class PlanService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Plan`;

  getMyPlans(): Observable<PlanModel[]> {
    return this.http.get<PlanModel[]>(`${this.apiUrl}/my-plans`);
  }

  getCuratedPlans(): Observable<PlanModel[]> {
    return this.http.get<PlanModel[]>(`${this.apiUrl}/curated`);
  }

  getPlan(id: number): Observable<PlanModel> {
    return this.http.get<PlanModel>(`${this.apiUrl}/${id}`);
  }

  createPlan(request: CreatePlanRequest): Observable<PlanModel> {
    return this.http.post<PlanModel>(this.apiUrl, request);
  }

  updatePlan(id: number, request: UpdatePlanRequest): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${id}`, request);
  }

  deletePlan(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  clonePlan(request: ClonePlanRequest): Observable<PlanModel> {
    return this.http.post<PlanModel>(`${this.apiUrl}/clone`, request);
  }
}
