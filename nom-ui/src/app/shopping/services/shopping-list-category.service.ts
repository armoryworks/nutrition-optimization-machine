import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ShoppingListCategory, ShoppingListCategoryCreate, ShoppingListBulkOperation } from '../models/shopping-list-category.model';

@Injectable({
    providedIn: 'root'
})
export class ShoppingListCategoryService {
    private http = inject(HttpClient);

    private readonly apiUrl = `${environment.apiUrl}/ShoppingListCategory`;



    getAllCategories(): Observable<ShoppingListCategory[]> {
        return this.http.get<ShoppingListCategory[]>(this.apiUrl);
    }

    getCategory(id: number): Observable<ShoppingListCategory> {
        return this.http.get<ShoppingListCategory>(`${this.apiUrl}/${id}`);
    }

    createCategory(category: ShoppingListCategoryCreate): Observable<ShoppingListCategory> {
        return this.http.post<ShoppingListCategory>(this.apiUrl, category);
    }

    updateCategory(id: number, category: ShoppingListCategoryCreate): Observable<ShoppingListCategory> {
        return this.http.put<ShoppingListCategory>(`${this.apiUrl}/${id}`, category);
    }

    deleteCategory(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }

    bulkOperation(operation: ShoppingListBulkOperation): Observable<{ message: string }> {
        return this.http.post<{ message: string }>(`${this.apiUrl}/bulk-operation`, operation);
    }
} 