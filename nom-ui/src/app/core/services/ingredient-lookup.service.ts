import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { IngredientLookupResult } from '../models/ingredient-lookup-result.model';

/** Ingredient search for the whole-food picker (directly-edible foods surface first). */
@Injectable({ providedIn: 'root' })
export class IngredientLookupService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/ingredients`;

  search(query: string): Observable<IngredientLookupResult[]> {
    return this.http.get<IngredientLookupResult[]>(`${this.apiUrl}/search`, {
      params: new HttpParams().set('q', query),
    });
  }
}
