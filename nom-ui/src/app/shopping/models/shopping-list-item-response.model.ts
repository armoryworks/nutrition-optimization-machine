// File: nom-ui/src/app/shopping/models/shopping-list-item-response.model.ts

import { IShoppingListItemResponseModel } from './shopping-list-item-response.model.interface';

export class ShoppingListItemResponseModel implements IShoppingListItemResponseModel {
    id = 0;
    shoppingListId = 0;
    ingredientId?: number;
    ingredientName = '';
    quantity = 0;
    measurementUnit = '';
    notes?: string;
    isCompleted = false;
    categoryId?: number;
    categoryName?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IShoppingListItemResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 