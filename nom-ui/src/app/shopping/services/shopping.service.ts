import { Injectable, inject } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { Observable } from "rxjs";
import { map } from "rxjs/operators";
import {
    ShoppingListCreateRequestModel,
    ShoppingListCreateResponseModel,
    ShoppingListResponseModel,
    ShoppingListItemCreateRequestModel,
    ShoppingListItemUpdateRequestModel,
    ShoppingListItemResponseModel,
} from "../models/shopping.model";
import { ShoppingListUpdateRequest } from "../models/shopping-list-update-request.model";
import { ShoppingListCategory, ShoppingListCategoryCreate } from "../models/shopping-list-category.model";

@Injectable({
    providedIn: "root",
})
export class ShoppingService {
    private http = inject(HttpClient);

    private readonly apiUrl = "/api/ShoppingList";
    private readonly categoryApiUrl = "/api/ShoppingListCategory";



    getShoppingLists(): Observable<ShoppingListResponseModel[]> {
        return this.http.get<ShoppingListResponseModel[]>(`${this.apiUrl}`);
    }

    createShoppingList(request: ShoppingListCreateRequestModel): Observable<ShoppingListCreateResponseModel> {
        return this.http.post<ShoppingListCreateResponseModel>(`${this.apiUrl}`, request);
    }

    getShoppingList(id: number): Observable<ShoppingListResponseModel> {
        return this.http.get<ShoppingListResponseModel>(`${this.apiUrl}/${id}`);
    }

    updateShoppingList(id: number, request: ShoppingListUpdateRequest): Observable<ShoppingListResponseModel> {
        return this.http.put<ShoppingListResponseModel>(`${this.apiUrl}/${id}`, request);
    }

    deleteShoppingList(id: number): Observable<void> {
        return this.http.delete(`${this.apiUrl}/${id}`).pipe(
            map(() => void 0)
        );
    }

    addItem(request: ShoppingListItemCreateRequestModel): Observable<ShoppingListItemResponseModel> {
        return this.http.post<ShoppingListItemResponseModel>(`${this.apiUrl}/item`, request);
    }

    updateItem(id: number, request: ShoppingListItemUpdateRequestModel): Observable<ShoppingListItemResponseModel> {
        return this.http.put<ShoppingListItemResponseModel>(`${this.apiUrl}/item/${id}`, request);
    }

    deleteItem(id: number): Observable<void> {
        return this.http.delete(`${this.apiUrl}/item/${id}`).pipe(
            map(() => void 0)
        );
    }

    // Wrapper methods for backward compatibility
    addShoppingListItem(shoppingListId: number, request: ShoppingListItemCreateRequestModel): Observable<ShoppingListItemResponseModel> {
        return this.addItem(request);
    }

    updateShoppingListItem(shoppingListId: number, itemId: number, request: ShoppingListItemUpdateRequestModel): Observable<ShoppingListItemResponseModel> {
        return this.updateItem(itemId, request);
    }

    deleteShoppingListItem(shoppingListId: number, itemId: number): Observable<void> {
        return this.deleteItem(itemId);
    }

    // Category management methods
    getCategories(): Observable<ShoppingListCategory[]> {
        return this.http.get<ShoppingListCategory[]>(`${this.categoryApiUrl}`);
    }

    createCategory(request: ShoppingListCategoryCreate): Observable<ShoppingListCategory> {
        return this.http.post<ShoppingListCategory>(`${this.categoryApiUrl}`, request);
    }

    updateCategory(id: number, request: ShoppingListCategoryCreate): Observable<ShoppingListCategory> {
        return this.http.put<ShoppingListCategory>(`${this.categoryApiUrl}/${id}`, request);
    }

    deleteCategory(id: number): Observable<void> {
        return this.http.delete(`${this.categoryApiUrl}/${id}`).pipe(
            map(() => void 0)
        );
    }
} 