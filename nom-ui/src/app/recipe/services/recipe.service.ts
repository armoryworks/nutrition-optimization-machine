// File: nom-ui/src/app/recipe/services/recipe.service.ts

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

import {
  RecipeModel,
  RecipeCreateModel,
  RecipeUpdateModel,
  RecipeCommentModel,
  RecipeRatingModel,
  RecipeCommentCreateModel,
  RecipeRatingCreateModel,
  RecipeRatingUpdateModel
} from '../models/recipe.model';

@Injectable({
  providedIn: 'root'
})
export class RecipeService {
  private apiUrl = `${environment.apiUrl}/recipe`;

  constructor(private http: HttpClient) { }

  getRecipes(): Observable<RecipeModel[]> {
    return this.http.get<RecipeModel[]>(this.apiUrl);
  }

  getMyIngredients(): Observable<any[]> {
    return this.http.get<any[]>(`${environment.apiUrl}/ingredient/my`);
  }

  getRecipe(id: number): Observable<RecipeModel> {
    return this.http.get<RecipeModel>(`${this.apiUrl}/${id}`);
  }

  createRecipe(recipe: RecipeCreateModel): Observable<RecipeModel> {
    return this.http.post<RecipeModel>(this.apiUrl, recipe);
  }

  updateRecipe(id: number, recipe: RecipeUpdateModel): Observable<RecipeModel> {
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
  searchIngredients(query: string): Observable<any[]> {
    return this.http.get<any[]>(`${environment.apiUrl}/ingredient/search?q=${query}`);
  }

  createIngredient(ingredient: any): Observable<any> {
    return this.http.post<any>(`${environment.apiUrl}/ingredient`, ingredient);
  }

  getIngredientDetails(id: number): Observable<any> {
    return this.http.get<any>(`${environment.apiUrl}/ingredient/${id}`);
  }

  getMeasurementTypes(): Observable<any[]> {
    return this.http.get<any[]>(`${environment.apiUrl}/measurement-types`);
  }

  // Recipe search
  searchRecipes(query: string): Observable<RecipeModel[]> {
    return this.http.get<RecipeModel[]>(`${this.apiUrl}/search?q=${query}`);
  }
}