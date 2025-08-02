// File: nom-ui/src/app/shopping/models/shopping-list-item-create-request.model.interface.ts

export interface IShoppingListItemCreateRequestModel {
    shoppingListId: number;
    ingredientId?: number;
    ingredientName: string;
    quantity: number;
    measurementUnit: string;
    notes?: string;
    categoryId?: number;
} 