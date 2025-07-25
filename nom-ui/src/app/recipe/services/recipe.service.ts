// File: nom-ui/src/app/recipe/services/recipe.service.ts

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { RecipeModel } from '../models/recipe.model';
import { IngredientSearchResponseModel } from '../models/ingredient-search-response.model';
import { IngredientModel } from '../models/ingredient.model';
import { CreateRecipeRequestModel } from '../models/create-recipe-request-model';

@Injectable({
  providedIn: 'root'
})
export class RecipeService {
  private readonly apiUrl = `/api/Recipe`;

  constructor(private http: HttpClient) { }

  searchIngredients(query: string): Observable<IngredientSearchResponseModel[]> {
    return this.http.get<IngredientSearchResponseModel[]>(`${this.apiUrl}/ingredients/search`, { params: { q: query } });
  }

  getIngredientDetails(id: number): Observable<IngredientModel> {
    return this.http.get<IngredientModel>(`${this.apiUrl}/ingredients/${id}`);
  }

  createRecipe(request: CreateRecipeRequestModel): Observable<RecipeModel> {
    return this.http.post<RecipeModel>(this.apiUrl, request);
  }

  createNewVersion(parentRecipeId: number): Observable<RecipeModel> {
    return this.http.post<RecipeModel>(`${this.apiUrl}/${parentRecipeId}/version`, {});
  }
}