// File: nom-ui/src/app/recipe/services/recipe.service.ts

import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeModel } from '../models/recipe.model';
import { CreateRecipeRequestModel } from '../models/create-recipe-request-model';
import { UpdateRecipeRequest } from '../models/update-recipe-request.model';
import { RecipeCommentModel } from '../models/recipe-comment.model';
import { RecipeCommentCreateModel } from '../models/recipe-comment-create.model';
import { RecipeRatingModel } from '../models/recipe-rating.model';
import { RecipeRatingCreateModel } from '../models/recipe-rating-create.model';
import { RecipeRatingUpdateModel } from '../models/recipe-rating-update.model';
import { RecipeSearchModel } from '../models/recipe-search.model';
import { RecipeSearchResponse } from '../models/recipe-search.model';
import { IngredientModel } from '../models/ingredient.model';
import { CreateIngredientRequestModel } from '../models/create-ingredient-request.model';
import { UpdateIngredientRequestModel } from '../models/update-ingredient-request.model';
import { ReferenceItemModel } from '../../common/models/reference-item.model';

@Injectable({
  providedIn: 'root'
})
export class RecipeService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/recipe`;

  // Recipe methods
  getRecipes(): Observable<RecipeModel[]> {
    return this.http.get<RecipeModel[]>(this.apiUrl);
  }

  getMyIngredients(): Observable<IngredientModel[]> {
    return this.http.get<IngredientModel[]>(`${environment.apiUrl}/ingredients/my`);
  }

  getRecipe(id: number): Observable<RecipeModel> {
    return this.http.get<RecipeModel>(`${this.apiUrl}/${id}`);
  }

  createRecipe(recipe: CreateRecipeRequestModel): Observable<RecipeModel> {
    return this.http.post<RecipeModel>(this.apiUrl, recipe);
  }

  updateRecipe(id: number, recipe: UpdateRecipeRequest): Observable<RecipeModel> {
    return this.http.put<RecipeModel>(`${this.apiUrl}/${id}`, recipe);
  }

  deleteRecipe(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  // Recipe Comments
  getComments(recipeId: number): Observable<RecipeCommentModel[]> {
    return this.http.get<RecipeCommentModel[]>(`${this.apiUrl}/${recipeId}/comments`);
  }

  addComment(comment: RecipeCommentCreateModel): Observable<RecipeCommentModel> {
    return this.http.post<RecipeCommentModel>(`${this.apiUrl}/${comment.recipeId}/comments`, comment);
  }

  deleteComment(commentId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/comments/${commentId}`);
  }

  // Recipe Ratings
  getRatings(recipeId: number): Observable<RecipeRatingModel[]> {
    return this.http.get<RecipeRatingModel[]>(`${this.apiUrl}/${recipeId}/ratings`);
  }

  addRating(rating: RecipeRatingCreateModel): Observable<RecipeRatingModel> {
    return this.http.post<RecipeRatingModel>(`${this.apiUrl}/${rating.recipeId}/ratings`, rating);
  }

  updateRating(ratingId: number, rating: RecipeRatingUpdateModel): Observable<RecipeRatingModel> {
    return this.http.put<RecipeRatingModel>(`${this.apiUrl}/ratings/${ratingId}`, rating);
  }

  deleteRating(ratingId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/ratings/${ratingId}`);
  }

  // Ingredient methods
  searchIngredients(query: string): Observable<IngredientModel[]> {
    return this.http.get<IngredientModel[]>(`${environment.apiUrl}/ingredients/search?q=${query}`);
  }

  createIngredient(ingredient: CreateIngredientRequestModel): Observable<IngredientModel> {
    return this.http.post<IngredientModel>(`${environment.apiUrl}/ingredients`, ingredient);
  }

  updateIngredient(id: number, ingredient: UpdateIngredientRequestModel): Observable<IngredientModel> {
    return this.http.put<IngredientModel>(`${environment.apiUrl}/ingredients/${id}`, ingredient);
  }

  getIngredientDetails(id: number): Observable<IngredientModel> {
    return this.http.get<IngredientModel>(`${environment.apiUrl}/ingredients/${id}`);
  }

  deleteIngredient(id: number): Observable<void> {
    return this.http.delete<void>(`${environment.apiUrl}/ingredients/${id}`);
  }



  // Recipe search
  searchRecipes(query: string): Observable<RecipeModel[]> {
    return this.http.get<RecipeModel[]>(`${this.apiUrl}/search?q=${query}`);
  }

  // Advanced recipe search with pagination
  searchRecipesAdvanced(searchParams: RecipeSearchModel): Observable<RecipeSearchResponse> {
    return this.http.post<RecipeSearchResponse>(`${this.apiUrl}/search`, searchParams);
  }

  // Get popular recipes
  getPopularRecipes(page = 1, pageSize = 10): Observable<RecipeSearchResponse> {
    return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/popular?page=${page}&pageSize=${pageSize}`);
  }

  // Get recent recipes  
  getRecentRecipes(page = 1, pageSize = 10): Observable<RecipeSearchResponse> {
    return this.http.get<RecipeSearchResponse>(`${this.apiUrl}/recent?page=${page}&pageSize=${pageSize}`);
  }

  // Additional methods for backward compatibility
  getMyRecipes(): Observable<RecipeModel[]> {
    return this.http.get<RecipeModel[]>(`${this.apiUrl}/my`);
  }

  // Recipe Categories
  getAllCategories(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/categories`);
  }

  createCategory(category: any): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/categories`, category);
  }

  deleteCategory(categoryId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/categories/${categoryId}`);
  }

  // Measurement Types
  getMeasurementTypes(): Observable<ReferenceItemModel[]> {
    return this.http.get<ReferenceItemModel[]>(`${environment.apiUrl}/measurement/all`);
  }

  getMeasurementsByCategory(categoryId: number): Observable<ReferenceItemModel[]> {
    return this.http.get<ReferenceItemModel[]>(`${environment.apiUrl}/measurement/by-category/${categoryId}`);
  }

  // Reference Data
  getReferencesByGroup(groupId: number): Observable<ReferenceItemModel[]> {
    return this.http.get<ReferenceItemModel[]>(`${environment.apiUrl}/reference/${groupId}/all`);
  }
}