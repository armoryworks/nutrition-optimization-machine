import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, catchError, shareReplay, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeSearchRequest } from '../models/recipe-search-request.model';
import { RecipeSearchResponse } from '../models/recipe-search-response.model';
import { RecipeSearchFilterOptions } from '../models/recipe-search-filter-options.model';

@Injectable({ providedIn: 'root' })
export class RecipeSearchService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/RecipeSearch`;
  private filterOptions$?: Observable<RecipeSearchFilterOptions>;

  search(request: RecipeSearchRequest): Observable<RecipeSearchResponse> {
    return this.http.post<RecipeSearchResponse>(`${this.apiUrl}/search`, request);
  }

  getFilterOptions(): Observable<RecipeSearchFilterOptions> {
    this.filterOptions$ ??= this.http
      .get<RecipeSearchFilterOptions>(`${this.apiUrl}/filter-options`)
      .pipe(
        catchError((err) => {
          this.filterOptions$ = undefined;
          return throwError(() => err);
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    return this.filterOptions$;
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

  getRandom(count = 1, householdId?: number, minCalories?: number, maxCalories?: number, recipeTypeId?: number, mealTypeId?: number): Observable<RecipeSearchResponse> {
    let params = new HttpParams().set('count', count.toString());
    if (householdId) params = params.set('householdId', householdId.toString());
    if (minCalories) params = params.set('minCalories', minCalories.toString());
    if (maxCalories) params = params.set('maxCalories', maxCalories.toString());
    if (recipeTypeId) params = params.set('recipeTypeId', recipeTypeId.toString());
    if (mealTypeId) params = params.set('mealTypeId', mealTypeId.toString());
    return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/random`, { params });
  }
}
