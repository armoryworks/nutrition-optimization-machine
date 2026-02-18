import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeModel } from '../models/recipe.model';

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
}
