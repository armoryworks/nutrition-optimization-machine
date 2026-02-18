import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ShoppingListModel } from '../models/shopping-list.model';
import { ShoppingListCreateRequestModel } from '../models/shopping-list-create-request.model';
import { ShoppingListUpdateRequest } from '../models/shopping-list-update-request.model';
import { ShoppingListItemCreateRequestModel } from '../models/shopping-list-item-create-request.model';

@Injectable({
    providedIn: 'root'
})
export class ShoppingListService {
    private http = inject(HttpClient);

    private apiUrl = `${environment.apiUrl}/ShoppingList`;



    // Get all shopping lists
    getAllShoppingLists(): Observable<ShoppingListModel[]> {
        return this.http.get<ShoppingListModel[]>(this.apiUrl);
    }

    // Get shopping list by ID
    getShoppingListById(id: number): Observable<ShoppingListModel> {
        return this.http.get<ShoppingListModel>(`${this.apiUrl}/${id}`);
    }

    // Create new shopping list
    createShoppingList(request: ShoppingListCreateRequestModel): Observable<ShoppingListModel> {
        return this.http.post<ShoppingListModel>(this.apiUrl, request);
    }

    // Update shopping list
    updateShoppingList(id: number, request: ShoppingListUpdateRequest): Observable<ShoppingListModel> {
        return this.http.put<ShoppingListModel>(`${this.apiUrl}/${id}`, request);
    }

    // Delete shopping list
    deleteShoppingList(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }

    // Get shopping lists by household
    getShoppingListsByHousehold(householdId: number): Observable<ShoppingListModel[]> {
        return this.http.get<ShoppingListModel[]>(`${this.apiUrl}/household/${householdId}`);
    }

    // Get active shopping lists
    getActiveShoppingLists(): Observable<ShoppingListModel[]> {
        return this.http.get<ShoppingListModel[]>(`${this.apiUrl}/active`);
    }

    // Toggle shopping list active status
    toggleShoppingListActive(id: number): Observable<ShoppingListModel> {
        return this.http.patch<ShoppingListModel>(`${this.apiUrl}/${id}/toggle-active`, {});
    }

    // Add items to shopping list
    addItemsToShoppingList(id: number, items: ShoppingListItemCreateRequestModel[]): Observable<ShoppingListModel> {
        return this.http.post<ShoppingListModel>(`${this.apiUrl}/${id}/items`, { items });
    }

    // Remove items from shopping list
    removeItemsFromShoppingList(id: number, itemIds: number[]): Observable<ShoppingListModel> {
        return this.http.delete<ShoppingListModel>(`${this.apiUrl}/${id}/items`, {
            body: { itemIds }
        });
    }

    // Clear shopping list
    clearShoppingList(id: number): Observable<ShoppingListModel> {
        return this.http.delete<ShoppingListModel>(`${this.apiUrl}/${id}/clear`);
    }

    // Duplicate shopping list
    duplicateShoppingList(id: number, newName: string): Observable<ShoppingListModel> {
        return this.http.post<ShoppingListModel>(`${this.apiUrl}/${id}/duplicate`, {
            newName
        });
    }
} 