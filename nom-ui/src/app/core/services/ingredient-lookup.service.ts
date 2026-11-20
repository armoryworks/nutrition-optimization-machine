import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { IngredientLookupResult } from '../models/ingredient-lookup-result.model';

/**
 * Ingredient search for the meal plan's food picker (directly-edible foods surface first).
 * It asks for prepared foods too, which only a plan may hold, never a recipe line.
 */
@Injectable({ providedIn: 'root' })
export class IngredientLookupService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/ingredients`;

  search(query: string): Observable<IngredientLookupResult[]> {
    return this.http.get<IngredientLookupResult[]>(`${this.apiUrl}/search`, {
      params: new HttpParams().set('q', query).set('prepared', 'true'),
    });
  }
}
