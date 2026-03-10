import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ShoppingListResponse } from '../models/shopping-list-response.model';
import { ShoppingListDetailResponse } from '../models/shopping-list-detail-response.model';
import { ShoppingListCreateRequest } from '../models/shopping-list-create-request.model';
import { ShoppingListCreateResponse } from '../models/shopping-list-create-response.model';
import { ShoppingListUpdateRequest } from '../models/shopping-list-update-request.model';
import { ShoppingListItemResponse } from '../models/shopping-list-item-response.model';
import { ShoppingListItemCreateRequest } from '../models/shopping-list-item-create-request.model';
import { ShoppingListItemUpdateRequest } from '../models/shopping-list-item-update-request.model';
import { ShoppingListRecipeAddRequest } from '../models/shopping-list-recipe-add-request.model';
import { ShoppingListShareRequest } from '../models/shopping-list-share-request.model';
import { ShoppingListCategoryResponse } from '../models/shopping-list-category-response.model';
import { ShoppingListCategoryCreateRequest } from '../models/shopping-list-category-create-request.model';
import { ShoppingListBulkOperationRequest } from '../models/shopping-list-bulk-operation-request.model';

@Injectable({ providedIn: 'root' })
export class ShoppingListService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/ShoppingList`;
  private readonly categoryApiUrl = `${environment.apiUrl}/ShoppingListCategory`;

  // ===== Shopping Lists =====

  getAll(): Observable<ShoppingListResponse[]> {
    return this.http.get<ShoppingListResponse[]>(this.apiUrl);
  }

  getById(id: number): Observable<ShoppingListDetailResponse> {
    return this.http.get<ShoppingListDetailResponse>(`${this.apiUrl}/${id}`);
  }

  create(request: ShoppingListCreateRequest): Observable<ShoppingListCreateResponse> {
    return this.http.post<ShoppingListCreateResponse>(this.apiUrl, request);
  }

  update(id: number, request: ShoppingListUpdateRequest): Observable<ShoppingListResponse> {
    return this.http.put<ShoppingListResponse>(`${this.apiUrl}/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  // ===== Items =====

  addItem(request: ShoppingListItemCreateRequest): Observable<ShoppingListItemResponse> {
    return this.http.post<ShoppingListItemResponse>(`${this.apiUrl}/item`, request);
  }

  updateItem(id: number, request: ShoppingListItemUpdateRequest): Observable<ShoppingListItemResponse> {
    return this.http.put<ShoppingListItemResponse>(`${this.apiUrl}/item/${id}`, request);
  }

  deleteItem(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/item/${id}`);
  }

  // ===== Recipe Integration =====

  addRecipeIngredients(listId: number, recipeId: number, request: ShoppingListRecipeAddRequest): Observable<ShoppingListDetailResponse> {
    return this.http.post<ShoppingListDetailResponse>(`${this.apiUrl}/${listId}/recipe/${recipeId}`, request);
  }

  removeRecipeIngredients(listId: number, recipeId: number): Observable<ShoppingListDetailResponse> {
    return this.http.delete<ShoppingListDetailResponse>(`${this.apiUrl}/${listId}/recipe/${recipeId}`);
  }

  // ===== Sharing =====

  shareList(listId: number, request: ShoppingListShareRequest): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${listId}/share`, request);
  }

  unshareList(listId: number, personId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${listId}/share/${personId}`);
  }

  // ===== Categories =====

  getCategories(): Observable<ShoppingListCategoryResponse[]> {
    return this.http.get<ShoppingListCategoryResponse[]>(this.categoryApiUrl);
  }

  getCategory(id: number): Observable<ShoppingListCategoryResponse> {
    return this.http.get<ShoppingListCategoryResponse>(`${this.categoryApiUrl}/${id}`);
  }

  createCategory(request: ShoppingListCategoryCreateRequest): Observable<ShoppingListCategoryResponse> {
    return this.http.post<ShoppingListCategoryResponse>(this.categoryApiUrl, request);
  }

  updateCategory(id: number, request: ShoppingListCategoryCreateRequest): Observable<ShoppingListCategoryResponse> {
    return this.http.put<ShoppingListCategoryResponse>(`${this.categoryApiUrl}/${id}`, request);
  }

  deleteCategory(id: number): Observable<void> {
    return this.http.delete<void>(`${this.categoryApiUrl}/${id}`);
  }

  bulkOperation(request: ShoppingListBulkOperationRequest): Observable<void> {
    return this.http.post<void>(`${this.categoryApiUrl}/bulk-operation`, request);
  }
}
