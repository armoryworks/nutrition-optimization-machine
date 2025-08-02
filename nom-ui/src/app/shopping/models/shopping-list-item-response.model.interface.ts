// File: nom-ui/src/app/shopping/models/shopping-list-item-response.model.interface.ts

export interface IShoppingListItemResponseModel {
    id: number;
    shoppingListId: number;
    ingredientId?: number;
    ingredientName: string;
    quantity: number;
    measurementUnit: string;
    notes?: string;
    isCompleted: boolean;
    categoryId?: number;
    categoryName?: string;
    createdDate: Date;
    modifiedDate?: Date;
} 