import { Injectable, inject } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { Observable } from "rxjs";
import { MealPlanCreateRequestModel } from "../models/meal-plan-create-request.model";
import { MealPlanCreateResponseModel } from "../models/meal-plan-create-response.model";
import { MealPlanResponseModel } from "../models/meal-plan-response.model";
import { MealPlanUpdateRequestModel } from "../models/meal-plan-update-request.model";
import { MealPlanRuleCreateRequestModel } from "../models/meal-plan-rule-create-request.model";
import { MealPlanRuleCreateResponseModel } from "../models/meal-plan-rule-create-response.model";
import { MealPlanRuleResponseModel } from "../models/meal-plan-rule-response.model";

@Injectable({
    providedIn: "root",
})
export class MealPlanService {
    private http = inject(HttpClient);

    private readonly apiUrl = "/api/MealPlan";



    getMealPlans(): Observable<MealPlanResponseModel[]> {
        return this.http.get<MealPlanResponseModel[]>(`${this.apiUrl}`);
    }

    createMealPlan(request: MealPlanCreateRequestModel): Observable<MealPlanCreateResponseModel> {
        return this.http.post<MealPlanCreateResponseModel>(`${this.apiUrl}`, request);
    }

    getMealPlan(id: number): Observable<MealPlanResponseModel> {
        return this.http.get<MealPlanResponseModel>(`${this.apiUrl}/${id}`);
    }

    updateMealPlan(id: number, request: MealPlanUpdateRequestModel): Observable<MealPlanResponseModel> {
        return this.http.put<MealPlanResponseModel>(`${this.apiUrl}/${id}`, request);
    }

    deleteMealPlan(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }

    createRule(request: MealPlanRuleCreateRequestModel): Observable<MealPlanRuleCreateResponseModel> {
        return this.http.post<MealPlanRuleCreateResponseModel>(`${this.apiUrl}/rule`, request);
    }

    getRule(id: number): Observable<MealPlanRuleResponseModel> {
        return this.http.get<MealPlanRuleResponseModel>(`${this.apiUrl}/rule/${id}`);
    }

    getRules(): Observable<MealPlanRuleResponseModel[]> {
        return this.http.get<MealPlanRuleResponseModel[]>(`${this.apiUrl}/rules`);
    }

    deleteRule(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/rule/${id}`);
    }
} 