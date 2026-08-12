import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeModel, RecipeVariationItemModel, RecipeDietMatchModel } from '../models/recipe.model';
import { RecipeCreateRequest } from '../models/recipe-create-request.model';
import { RecipeCreateResponse } from '../models/recipe-create-response.model';
import { RecipeUpdateRequest } from '../models/recipe-update-request.model';
import { RecipeAssetResponse } from '../models/recipe-asset-response.model';
import { RecipeCommentResponseModel } from '../models/recipe-comment-response.model';
import { RecipeRatingResponseModel } from '../models/recipe-rating-response.model';
import {
  RecipeSubstitutionModel,
  RecipeAugmentationModel,
} from '../models/recipe-substitution.model';

@Injectable({ providedIn: 'root' })
export class RecipeService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Recipe`;

  getRecipe(id: number): Observable<RecipeModel> {
    return this.http.get<RecipeModel>(`${this.apiUrl}/${id}`);
  }

  saveVariation(recipeId: number, items: { ingredientId: number; substituteIngredientId: number }[]): Observable<RecipeVariationItemModel[]> {
    return this.http.put<RecipeVariationItemModel[]>(`${this.apiUrl}/${recipeId}/variation`, items);
  }

  deleteVariation(recipeId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${recipeId}/variation`);
  }

  getDietMatches(recipeId: number): Observable<RecipeDietMatchModel[]> {
    return this.http.get<RecipeDietMatchModel[]>(`${this.apiUrl}/${recipeId}/diet`);
  }

  getSubstitutions(recipeId: number): Observable<RecipeSubstitutionModel[]> {
    return this.http.get<RecipeSubstitutionModel[]>(`${this.apiUrl}/${recipeId}/substitutions`);
  }

  getAugmentations(recipeId: number): Observable<RecipeAugmentationModel[]> {
    return this.http.get<RecipeAugmentationModel[]>(`${this.apiUrl}/${recipeId}/augmentations`);
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

  // Comments
  getComments(recipeId: number): Observable<RecipeCommentResponseModel[]> {
    return this.http.get<RecipeCommentResponseModel[]>(
      `${environment.apiUrl}/recipe/${recipeId}/comments`
    );
  }

  addComment(recipeId: number, comment: string): Observable<RecipeCommentResponseModel> {
    return this.http.post<RecipeCommentResponseModel>(
      `${environment.apiUrl}/recipe/${recipeId}/comments`,
      { comment }
    );
  }

  deleteComment(commentId: number): Observable<void> {
    return this.http.delete<void>(
      `${environment.apiUrl}/recipe/comments/${commentId}`
    );
  }

  // Ratings
  getRatings(recipeId: number): Observable<RecipeRatingResponseModel[]> {
    return this.http.get<RecipeRatingResponseModel[]>(
      `${environment.apiUrl}/recipe/${recipeId}/ratings`
    );
  }

  addRating(recipeId: number, rating: number): Observable<RecipeRatingResponseModel> {
    return this.http.post<RecipeRatingResponseModel>(
      `${environment.apiUrl}/recipe/${recipeId}/ratings`,
      { rating }
    );
  }

  updateRating(ratingId: number, rating: number): Observable<RecipeRatingResponseModel> {
    return this.http.put<RecipeRatingResponseModel>(
      `${environment.apiUrl}/recipe/ratings/${ratingId}`,
      { rating }
    );
  }
}
