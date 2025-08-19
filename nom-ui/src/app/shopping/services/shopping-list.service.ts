import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ShoppingList } from '../models/shopping-list.model';
import { ShoppingListCreateRequest } from '../models/shopping-list-create-request.model';
import { ShoppingListUpdateRequest } from '../models/shopping-list-update-request.model';
import { ShoppingListItemCreateRequestModel } from '../models/shopping-list-item-create-request.model';

@Injectable({
    providedIn: 'root'
})
export class ShoppingListService {
    private http = inject(HttpClient);

    private apiUrl = `${environment.apiUrl}/api/shopping-lists`;



    // Get all shopping lists
    getAllShoppingLists(): Observable<ShoppingList[]> {
        return this.http.get<ShoppingList[]>(this.apiUrl);
    }

    // Get shopping list by ID
    getShoppingListById(id: number): Observable<ShoppingList> {
        return this.http.get<ShoppingList>(`${this.apiUrl}/${id}`);
    }

    // Create new shopping list
    createShoppingList(request: ShoppingListCreateRequest): Observable<ShoppingList> {
        return this.http.post<ShoppingList>(this.apiUrl, request);
    }

    // Update shopping list
    updateShoppingList(id: number, request: ShoppingListUpdateRequest): Observable<ShoppingList> {
        return this.http.put<ShoppingList>(`${this.apiUrl}/${id}`, request);
    }

    // Delete shopping list
    deleteShoppingList(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }

    // Get shopping lists by household
    getShoppingListsByHousehold(householdId: number): Observable<ShoppingList[]> {
        return this.http.get<ShoppingList[]>(`${this.apiUrl}/household/${householdId}`);
    }

    // Get active shopping lists
    getActiveShoppingLists(): Observable<ShoppingList[]> {
        return this.http.get<ShoppingList[]>(`${this.apiUrl}/active`);
    }

    // Toggle shopping list active status
    toggleShoppingListActive(id: number): Observable<ShoppingList> {
        return this.http.patch<ShoppingList>(`${this.apiUrl}/${id}/toggle-active`, {});
    }

    // Add items to shopping list
    addItemsToShoppingList(id: number, items: ShoppingListItemCreateRequestModel[]): Observable<ShoppingList> {
        return this.http.post<ShoppingList>(`${this.apiUrl}/${id}/items`, { items });
    }

    // Remove items from shopping list
    removeItemsFromShoppingList(id: number, itemIds: number[]): Observable<ShoppingList> {
        return this.http.delete<ShoppingList>(`${this.apiUrl}/${id}/items`, {
            body: { itemIds }
        });
    }

    // Clear shopping list
    clearShoppingList(id: number): Observable<ShoppingList> {
        return this.http.delete<ShoppingList>(`${this.apiUrl}/${id}/clear`);
    }

    // Duplicate shopping list
    duplicateShoppingList(id: number, newName: string): Observable<ShoppingList> {
        return this.http.post<ShoppingList>(`${this.apiUrl}/${id}/duplicate`, {
            newName
        });
    }
} 