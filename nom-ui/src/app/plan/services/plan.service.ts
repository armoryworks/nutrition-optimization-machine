import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PlanModel } from '../models/plan.model';

@Injectable({
    providedIn: 'root'
})
export class PlanService {
    private readonly apiUrl = `/api/Plan`;

    constructor(private http: HttpClient) { }

    getCuratedPlans(): Observable<PlanModel[]> {
        return this.http.get<PlanModel[]>(`${this.apiUrl}/curated`);
    }

    getMyPlans(): Observable<PlanModel[]> {
        return this.http.get<PlanModel[]>(`${this.apiUrl}/my-plans`);
    }

    getPlanById(id: number): Observable<PlanModel> {
        return this.http.get<PlanModel>(`${this.apiUrl}/${id}`);
    }

    clonePlan(sourcePlanId: number, newPlanName: string): Observable<PlanModel> {
        return this.http.post<PlanModel>(`${this.apiUrl}/clone`, {
            sourcePlanId,
            newPlanName
        });
    }

    createPlan(plan: any): Observable<PlanModel> {
        return this.http.post<PlanModel>(`${this.apiUrl}`, plan);
    }

    updatePlan(id: number, plan: any): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/${id}`, plan);
    }

    deletePlan(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }

    submitPlanForCuration(id: number): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/${id}/submit-for-curation`, {});
    }
} 