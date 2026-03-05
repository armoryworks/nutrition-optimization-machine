import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CookbookResponseModel, CookbookCreateRequest, CookbookUpdateRequest } from '../models/cookbook.model';
import { RecipeModel } from '../models/recipe.model';

@Injectable({ providedIn: 'root' })
export class CookbookService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Cookbook`;

  getCookbooks(householdId: number): Observable<CookbookResponseModel[]> {
    const params = new HttpParams().set('householdId', householdId.toString());
    return this.http.get<CookbookResponseModel[]>(this.apiUrl, { params });
  }

  getCookbook(id: number): Observable<CookbookResponseModel> {
    return this.http.get<CookbookResponseModel>(`${this.apiUrl}/${id}`);
  }

  createCookbook(request: CookbookCreateRequest): Observable<number> {
    return this.http.post<number>(this.apiUrl, request);
  }

  updateCookbook(id: number, request: CookbookUpdateRequest): Observable<CookbookResponseModel> {
    return this.http.put<CookbookResponseModel>(`${this.apiUrl}/${id}`, request);
  }

  deleteCookbook(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  addRecipe(cookbookId: number, recipeId: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${cookbookId}/recipe/${recipeId}`, {});
  }

  removeRecipe(cookbookId: number, recipeId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${cookbookId}/recipe/${recipeId}`);
  }

  getRecipes(cookbookId: number): Observable<RecipeModel[]> {
    return this.http.get<RecipeModel[]>(`${this.apiUrl}/${cookbookId}/recipes`);
  }
}
