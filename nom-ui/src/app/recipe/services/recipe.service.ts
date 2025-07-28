// File: nom-ui/src/app/recipe/services/recipe.service.ts

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { RecipeModel } from '../models/recipe.model';
import { IngredientSearchResponseModel } from '../models/ingredient-search-response.model';
import { IngredientModel } from '../models/ingredient.model';
import { CreateRecipeRequestModel } from '../models/create-recipe-request-model';
import { RecipeDashboardItemModel } from '../models/recipe-dashboard-item.model';
import { ReferenceItemModel } from '../../common/models/reference-item.model';
import { UpdateRecipeRequest } from '../models/update-recipe-request.model';

interface SearchResult {
  id: number;
  name: string;
  type: 'Recipe' | 'Ingredient';
  curationStatus: string;
  isCurated: boolean;
  authorName?: string;
  description?: string;
}

@Injectable({
  providedIn: 'root'
})
export class RecipeService {
  private readonly apiUrl = `/api/Recipe`;
  private readonly referenceApiUrl = `/api/Reference`;


  constructor(private http: HttpClient) { }

  searchIngredients(query: string): Observable<IngredientSearchResponseModel[]> {
    return this.http.get<IngredientSearchResponseModel[]>(`${this.apiUrl}/ingredients/search`, { params: { q: query } });
  }

  searchRecipes(query: string): Observable<SearchResult[]> {
    // TODO: Replace with actual API call when backend is ready
    // For now, return mock data
    const mockResults: SearchResult[] = [
      {
        id: 1,
        name: 'Chicken Caesar Salad',
        type: 'Recipe',
        curationStatus: 'Curated',
        isCurated: true,
        authorName: 'Chef John',
        description: 'A classic Caesar salad with grilled chicken breast, romaine lettuce, and traditional Caesar dressing.'
      },
      {
        id: 2,
        name: 'Fresh Basil',
        type: 'Ingredient',
        curationStatus: 'Curated',
        isCurated: true,
        authorName: 'Garden Fresh Co.',
        description: 'Fresh basil leaves with aromatic flavor, perfect for Italian dishes and pesto.'
      },
      {
        id: 3,
        name: 'My Homemade Pizza',
        type: 'Recipe',
        curationStatus: 'NonCurated',
        isCurated: false,
        authorName: 'Current User',
        description: 'A personal recipe for homemade pizza with custom toppings.'
      }
    ];

    // Filter results based on query
    const filteredResults = mockResults.filter(result =>
      result.name.toLowerCase().includes(query.toLowerCase()) ||
      result.description?.toLowerCase().includes(query.toLowerCase())
    );

    return of(filteredResults);
  }

  getIngredientDetails(id: number): Observable<IngredientModel> {
    return this.http.get<IngredientModel>(`${this.apiUrl}/ingredients/${id}`);
  }

  createRecipe(request: CreateRecipeRequestModel): Observable<RecipeModel> {
    return this.http.post<RecipeModel>(this.apiUrl, request);
  }

  updateRecipe(id: number, request: UpdateRecipeRequest): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${id}`, request);
  }

  createNewVersion(parentRecipeId: number): Observable<RecipeModel> {
    return this.http.post<RecipeModel>(`${this.apiUrl}/${parentRecipeId}/version`, {});
  }

  getMyRecipes(): Observable<RecipeDashboardItemModel[]> {
    return this.http.get<RecipeDashboardItemModel[]>(`${this.apiUrl}/my-recipes`);
  }

  getMyIngredients(): Observable<RecipeDashboardItemModel[]> {
    return this.http.get<RecipeDashboardItemModel[]>(`${this.apiUrl}/my-ingredients`);
  }

  getMeasurementTypes(): Observable<ReferenceItemModel[]> {
    return this.http.get<ReferenceItemModel[]>(`${this.referenceApiUrl}/measurement-types`);
  }
}