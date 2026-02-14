import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
    SmartShoppingListRequestModel,
    SmartShoppingListResponseModel,
    SmartShoppingListItemModel,
    AIShoppingListRequestModel,
    AIShoppingListResponseModel,
    ShoppingListOptimizationModel,
    ShoppingListSuggestionModel,
    ShoppingListAnalyticsModel,
    ShoppingListTemplateModel,
    ShoppingListGenerationHistoryModel,
} from '../models/smart-shopping-list.models';

@Injectable({
    providedIn: 'root'
})
export class SmartShoppingListService {
    private http = inject(HttpClient);

    private readonly apiUrl = `${environment.apiUrl}/smartshoppinglist`;

    generateSmartShoppingList(request: SmartShoppingListRequestModel): Observable<SmartShoppingListResponseModel> {
        return this.http.post<SmartShoppingListResponseModel>(`${this.apiUrl}/generate`, request);
    }

    generateAIShoppingList(request: AIShoppingListRequestModel): Observable<AIShoppingListResponseModel> {
        return this.http.post<AIShoppingListResponseModel>(`${this.apiUrl}/ai-generate`, request);
    }

    optimizeShoppingList(request: ShoppingListOptimizationModel): Observable<SmartShoppingListResponseModel> {
        return this.http.post<SmartShoppingListResponseModel>(`${this.apiUrl}/optimize`, request);
    }

    getSuggestions(shoppingListId: number): Observable<ShoppingListSuggestionModel[]> {
        return this.http.get<ShoppingListSuggestionModel[]>(`${this.apiUrl}/${shoppingListId}/suggestions`);
    }

    getAnalytics(shoppingListId: number): Observable<ShoppingListAnalyticsModel> {
        return this.http.get<ShoppingListAnalyticsModel>(`${this.apiUrl}/${shoppingListId}/analytics`);
    }

    getTemplates(): Observable<ShoppingListTemplateModel[]> {
        return this.http.get<ShoppingListTemplateModel[]>(`${this.apiUrl}/templates`);
    }

    createTemplate(template: ShoppingListTemplateModel): Observable<ShoppingListTemplateModel> {
        return this.http.post<ShoppingListTemplateModel>(`${this.apiUrl}/templates`, template);
    }

    getGenerationHistory(shoppingListId: number): Observable<ShoppingListGenerationHistoryModel[]> {
        return this.http.get<ShoppingListGenerationHistoryModel[]>(`${this.apiUrl}/${shoppingListId}/history`);
    }

    mergeItems(items: SmartShoppingListItemModel[]): Observable<SmartShoppingListItemModel[]> {
        return this.http.post<SmartShoppingListItemModel[]>(`${this.apiUrl}/merge-items`, items);
    }

    suggestSubstitutions(items: SmartShoppingListItemModel[]): Observable<ShoppingListSuggestionModel[]> {
        return this.http.post<ShoppingListSuggestionModel[]>(`${this.apiUrl}/substitutions`, items);
    }

    estimateCost(items: SmartShoppingListItemModel[]): Observable<number> {
        return this.http.post<number>(`${this.apiUrl}/estimate-cost`, items);
    }

    getNutritionalAnalysis(items: SmartShoppingListItemModel[]): Observable<Record<string, unknown>> {
        return this.http.post<Record<string, unknown>>(`${this.apiUrl}/nutritional-analysis`, items);
    }
}
