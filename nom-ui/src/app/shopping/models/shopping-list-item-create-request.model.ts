// File: nom-ui/src/app/shopping/models/shopping-list-item-create-request.model.ts

import { IShoppingListItemCreateRequestModel } from './shopping-list-item-create-request.model.interface';

export class ShoppingListItemCreateRequestModel implements IShoppingListItemCreateRequestModel {
    shoppingListId = 0;
    ingredientId?: number;
    ingredientName = '';
    quantity = 0;
    measurementUnit = '';
    notes?: string;
    categoryId?: number;

    constructor(data?: Partial<IShoppingListItemCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 