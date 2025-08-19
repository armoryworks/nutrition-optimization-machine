import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeSearchModel, RecipeSearchResponse } from '../models/recipe-search.model';

@Injectable({
    providedIn: 'root'
})
export class RecipeSearchService {
    private http = inject(HttpClient);

    private readonly apiUrl = `${environment.apiUrl}/RecipeSearch`;



    searchRecipes(searchModel: RecipeSearchModel): Observable<RecipeSearchResponse> {
        return this.http.post<RecipeSearchResponse>(`${this.apiUrl}/search`, searchModel);
    }

    getSearchSuggestions(query: string): Observable<string[]> {
        const params = new HttpParams().set('query', query);
        return this.http.get<string[]>(`${this.apiUrl}/suggestions`, { params });
    }

    getPopularRecipes(count = 10): Observable<RecipeSearchResponse> {
        const params = new HttpParams().set('count', count.toString());
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/popular`, { params });
    }

    getRecentRecipes(count = 10): Observable<RecipeSearchResponse> {
        const params = new HttpParams().set('count', count.toString());
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/recent`, { params });
    }

    getRecipesByIngredients(ingredientIds: number[], count = 20): Observable<RecipeSearchResponse> {
        const params = new HttpParams().set('count', count.toString());
        return this.http.post<RecipeSearchResponse>(`${this.apiUrl}/by-ingredients`, ingredientIds, { params });
    }
} 