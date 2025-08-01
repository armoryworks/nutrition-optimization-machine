import { Injectable } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { Observable } from "rxjs";
import {
    ShoppingListCreateRequestModel,
    ShoppingListCreateResponseModel,
    ShoppingListResponseModel,
    ShoppingListItemCreateRequestModel,
    ShoppingListItemUpdateRequestModel,
    ShoppingListItemResponseModel,
} from "../models/shopping.model";

@Injectable({
    providedIn: "root",
})
export class ShoppingService {
    private readonly apiUrl = "/api/ShoppingList";

    constructor(private http: HttpClient) { }

    createShoppingList(request: ShoppingListCreateRequestModel): Observable<ShoppingListCreateResponseModel> {
        return this.http.post<ShoppingListCreateResponseModel>(`${this.apiUrl}`, request);
    }

    getShoppingList(id: number): Observable<ShoppingListResponseModel> {
        return this.http.get<ShoppingListResponseModel>(`${this.apiUrl}/${id}`);
    }

    updateShoppingList(id: number, request: ShoppingListCreateRequestModel): Observable<ShoppingListResponseModel> {
        return this.http.put<ShoppingListResponseModel>(`${this.apiUrl}/${id}`, request);
    }

    deleteShoppingList(id: number): Observable<any> {
        return this.http.delete(`${this.apiUrl}/${id}`);
    }

    addItem(request: ShoppingListItemCreateRequestModel): Observable<ShoppingListItemResponseModel> {
        return this.http.post<ShoppingListItemResponseModel>(`${this.apiUrl}/item`, request);
    }

    updateItem(id: number, request: ShoppingListItemUpdateRequestModel): Observable<ShoppingListItemResponseModel> {
        return this.http.put<ShoppingListItemResponseModel>(`${this.apiUrl}/item/${id}`, request);
    }

    deleteItem(id: number): Observable<any> {
        return this.http.delete(`${this.apiUrl}/item/${id}`);
    }
} 