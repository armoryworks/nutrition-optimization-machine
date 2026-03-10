import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeModel } from '../models/recipe.model';
import { RecipeCreateRequest } from '../models/recipe-create-request.model';
import { RecipeCreateResponse } from '../models/recipe-create-response.model';
import { RecipeUpdateRequest } from '../models/recipe-update-request.model';
import { RecipeAssetResponse } from '../models/recipe-asset-response.model';
import { RecipeCommentResponseModel } from '../models/recipe-comment-response.model';
import { RecipeRatingResponseModel } from '../models/recipe-rating-response.model';

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
