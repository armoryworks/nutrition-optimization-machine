import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeSearchRequest, RecipeSearchResponse } from '../models/recipe-search.model';

@Injectable({ providedIn: 'root' })
export class RecipeSearchService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/RecipeSearch`;

  search(request: RecipeSearchRequest): Observable<RecipeSearchResponse> {
    return this.http.post<RecipeSearchResponse>(`${this.apiUrl}/search`, request);
  }

  getSuggestions(query: string): Observable<string[]> {
    const params = new HttpParams().set('query', query);
    return this.http.get<string[]>(`${this.apiUrl}/suggestions`, { params });
  }

  getPopular(count = 20): Observable<RecipeSearchResponse> {
    const params = new HttpParams().set('count', count.toString());
    return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/popular`, { params });
  }

  getRecent(count = 20): Observable<RecipeSearchResponse> {
    const params = new HttpParams().set('count', count.toString());
    return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/recent`, { params });
  }

  getRandom(count = 1, householdId?: number, minCalories?: number, maxCalories?: number, recipeTypeId?: number): Observable<RecipeSearchResponse> {
    let params = new HttpParams().set('count', count.toString());
    if (householdId) params = params.set('householdId', householdId.toString());
    if (minCalories) params = params.set('minCalories', minCalories.toString());
    if (maxCalories) params = params.set('maxCalories', maxCalories.toString());
    if (recipeTypeId) params = params.set('recipeTypeId', recipeTypeId.toString());
    return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/random`, { params });
  }
}
