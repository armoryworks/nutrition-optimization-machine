import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeSearchResponse } from '../models/recipe-search.model';
import {
    RecipeAdvancedSearchModel,
    RecipeSuggestionQueryModel,
    RecipeSuggestionResponseModel,
} from '../models/recipe-advanced-search.model';

@Injectable({
    providedIn: 'root'
})
export class RecipeAdvancedSearchService {
    private http = inject(HttpClient);

    private readonly apiUrl = `${environment.apiUrl}/recipe-advanced-search`;

    fuzzySearch(query: string, page = 1, pageSize = 20): Observable<RecipeSearchResponse> {
        const params = new HttpParams()
            .set('query', query)
            .set('page', page.toString())
            .set('pageSize', pageSize.toString());
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/fuzzy`, { params });
    }

    advancedSearch(searchModel: RecipeAdvancedSearchModel): Observable<RecipeSearchResponse> {
        return this.http.post<RecipeSearchResponse>(`${this.apiUrl}/advanced`, searchModel);
    }

    getSuggestions(suggestionModel: RecipeSuggestionQueryModel): Observable<RecipeSuggestionResponseModel> {
        return this.http.post<RecipeSuggestionResponseModel>(`${this.apiUrl}/suggestions`, suggestionModel);
    }

    searchByCategories(categoryIds: number[], page = 1, pageSize = 20): Observable<RecipeSearchResponse> {
        let params = new HttpParams()
            .set('page', page.toString())
            .set('pageSize', pageSize.toString());
        categoryIds.forEach(id => params = params.append('categoryIds', id.toString()));
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/by-categories`, { params });
    }

    searchByTags(tagIds: number[], page = 1, pageSize = 20): Observable<RecipeSearchResponse> {
        let params = new HttpParams()
            .set('page', page.toString())
            .set('pageSize', pageSize.toString());
        tagIds.forEach(id => params = params.append('tagIds', id.toString()));
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/by-tags`, { params });
    }

    searchByTools(toolIds: number[], page = 1, pageSize = 20): Observable<RecipeSearchResponse> {
        let params = new HttpParams()
            .set('page', page.toString())
            .set('pageSize', pageSize.toString());
        toolIds.forEach(id => params = params.append('toolIds', id.toString()));
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/by-tools`, { params });
    }

    getPopularRecipes(count = 10): Observable<RecipeSearchResponse> {
        const params = new HttpParams().set('count', count.toString());
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/popular`, { params });
    }

    getRecentRecipes(count = 10): Observable<RecipeSearchResponse> {
        const params = new HttpParams().set('count', count.toString());
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/recent`, { params });
    }

    searchByIngredients(ingredientIds: number[], count = 20): Observable<RecipeSearchResponse> {
        let params = new HttpParams().set('count', count.toString());
        ingredientIds.forEach(id => params = params.append('ingredientIds', id.toString()));
        return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/by-ingredients`, { params });
    }
}
