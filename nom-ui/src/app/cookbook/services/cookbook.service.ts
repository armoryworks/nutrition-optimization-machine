import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

import { CookbookResponseModel } from '../models/cookbook-response.model';
import { CookbookCreateRequestModel } from '../models/cookbook-create-request.model';
import { CookbookUpdateRequestModel } from '../models/cookbook-update-request.model';
import { RecipeDashboardItemModel } from '../../recipe/models/recipe-dashboard-item.model';

@Injectable({
    providedIn: 'root'
})
export class CookbookService {
    private http = inject(HttpClient);

    private apiUrl = `${environment.apiUrl}/cookbook`;

    getCookbooks(householdId: number): Observable<CookbookResponseModel[]> {
        return this.http.get<CookbookResponseModel[]>(this.apiUrl, { params: { householdId } });
    }

    getCookbook(id: number): Observable<CookbookResponseModel> {
        return this.http.get<CookbookResponseModel>(`${this.apiUrl}/${id}`);
    }

    createCookbook(request: CookbookCreateRequestModel): Observable<number> {
        return this.http.post<number>(this.apiUrl, request);
    }

    updateCookbook(id: number, request: CookbookUpdateRequestModel): Observable<CookbookResponseModel> {
        return this.http.put<CookbookResponseModel>(`${this.apiUrl}/${id}`, request);
    }

    deleteCookbook(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }

    addRecipe(cookbookId: number, recipeId: number): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/${cookbookId}/recipe/${recipeId}`, {});
    }

    removeRecipe(cookbookId: number, recipeId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${cookbookId}/recipe/${recipeId}`);
    }

    getCookbookRecipes(cookbookId: number): Observable<RecipeDashboardItemModel[]> {
        return this.http.get<RecipeDashboardItemModel[]>(`${this.apiUrl}/${cookbookId}/recipes`);
    }
}
