import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeSearchModel, RecipeSearchResponse } from '../models/recipe-search.model';

@Injectable({
    providedIn: 'root'
})
export class RecipeSearchService {
    private readonly apiUrl = `${environment.apiUrl}/RecipeSearch`;

    constructor(private http: HttpClient) { }

    searchRecipes(searchModel: RecipeSearchModel): Observable<RecipeSearchResponse> {
        return this.http.post<RecipeSearchResponse>(`${this.apiUrl}/search`, searchModel);
    }

    getSearchSuggestions(query: string): Observable<string[]> {
        const params = new HttpParams().set('query', query);
        return this.http.get<string[]>(`${this.apiUrl}/suggestions`, { params });
    }

    getPopularRecipes(count: number = 10): Observable<RecipeSearchResponse> {
        const params = new HttpParams().set('count', count.toString());
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/popular`, { params });
    }

    getRecentRecipes(count: number = 10): Observable<RecipeSearchResponse> {
        const params = new HttpParams().set('count', count.toString());
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/recent`, { params });
    }

    getRecipesByIngredients(ingredientIds: number[], count: number = 20): Observable<RecipeSearchResponse> {
        const params = new HttpParams().set('count', count.toString());
        return this.http.post<RecipeSearchResponse>(`${this.apiUrl}/by-ingredients`, ingredientIds, { params });
    }
} 