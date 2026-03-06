import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeModel, RecipeCreateRequest, RecipeCreateResponse, RecipeUpdateRequest, RecipeAssetResponse } from '../models/recipe.model';

@Injectable({ providedIn: 'root' })
export class RecipeService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Recipe`;

  getRecipe(id: number): Observable<RecipeModel> {
    return this.http.get<RecipeModel>(`${this.apiUrl}/${id}`);
  }

  getRecipes(): Observable<RecipeModel[]> {
    return this.http.get<RecipeModel[]>(this.apiUrl);
  }

  getMyRecipes(): Observable<RecipeModel[]> {
    return this.http.get<RecipeModel[]>(`${this.apiUrl}/my`);
  }

  createRecipe(request: RecipeCreateRequest): Observable<RecipeCreateResponse> {
    return this.http.post<RecipeCreateResponse>(this.apiUrl, request);
  }

  updateRecipe(id: number, request: RecipeUpdateRequest): Observable<RecipeModel> {
    return this.http.put<RecipeModel>(`${this.apiUrl}/${id}`, request);
  }

  deleteRecipe(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  uploadImage(recipeId: number, file: File): Observable<RecipeAssetResponse> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<RecipeAssetResponse>(`${this.apiUrl}/${recipeId}/image`, formData);
  }

  deleteImage(recipeId: number, assetId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${recipeId}/image/${assetId}`);
  }

  getAssets(recipeId: number): Observable<RecipeAssetResponse[]> {
    return this.http.get<RecipeAssetResponse[]>(`${this.apiUrl}/${recipeId}/assets`);
  }
}
