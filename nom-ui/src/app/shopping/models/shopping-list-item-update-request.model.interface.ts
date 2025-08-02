// File: nom-ui/src/app/shopping/models/shopping-list-item-update-request.model.interface.ts

export interface IShoppingListItemUpdateRequestModel {
    ingredientName: string;
    quantity: number;
    measurementUnit: string;
    notes?: string;
    isCompleted: boolean;
    categoryId?: number;
} 