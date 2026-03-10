import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { IngredientSearchResult } from '../models/ingredient-search-result.model';
import { IngredientEditModel } from '../models/ingredient-edit.model';
import { CreateIngredientRequest } from '../models/create-ingredient-request.model';
import { UpdateIngredientRequest } from '../models/update-ingredient-request.model';

@Injectable({ providedIn: 'root' })
export class IngredientService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Ingredients`;

  searchIngredients(query: string): Observable<IngredientSearchResult[]> {
    return this.http.get<IngredientSearchResult[]>(`${this.apiUrl}/search`, {
      params: { q: query },
    });
  }

  getIngredient(id: number): Observable<IngredientEditModel> {
    return this.http.get<IngredientEditModel>(`${this.apiUrl}/${id}`);
  }

  getMyIngredients(): Observable<IngredientEditModel[]> {
    return this.http.get<IngredientEditModel[]>(`${this.apiUrl}/my`);
  }

  createIngredient(request: CreateIngredientRequest): Observable<IngredientEditModel> {
    return this.http.post<IngredientEditModel>(this.apiUrl, request);
  }

  updateIngredient(id: number, request: UpdateIngredientRequest): Observable<IngredientEditModel> {
    return this.http.put<IngredientEditModel>(`${this.apiUrl}/${id}`, request);
  }
}
