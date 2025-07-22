// File: nom-ui/src/app/recipe/services/recipe.service.ts

import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { IngredientSearchResponseModel } from '../models/ingredient-search-response.model';
import { IngredientModel } from '../models/ingredient.model';

@Injectable({
  providedIn: 'root',
})
export class RecipeService {
  private readonly apiUrl = '/api/Recipe';

  constructor(private http: HttpClient) {}

  /**
   * Searches for ingredients based on a search term.
   * @param searchTerm The term to search for.
   */
  searchIngredients(
    searchTerm: string
  ): Observable<IngredientSearchResponseModel[]> {
    const params = new HttpParams().set('q', searchTerm);
    return this.http.get<IngredientSearchResponseModel[]>(
      `${this.apiUrl}/ingredients/search`,
      { params }
    );
  }

  /**
   * Retrieves the detailed nutritional information for a specific ingredient.
   * @param id The ID of the ingredient.
   */
  getIngredientDetails(id: number): Observable<IngredientModel> {
    return this.http.get<IngredientModel>(`${this.apiUrl}/ingredients/${id}`);
  }
}
