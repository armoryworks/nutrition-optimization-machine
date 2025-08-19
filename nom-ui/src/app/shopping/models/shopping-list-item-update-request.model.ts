// File: nom-ui/src/app/shopping/models/shopping-list-item-update-request.model.ts

import { IShoppingListItemUpdateRequestModel } from './shopping-list-item-update-request.model.interface';

export class ShoppingListItemUpdateRequestModel implements IShoppingListItemUpdateRequestModel {
    ingredientName = '';
    quantity = 0;
    measurementUnit = '';
    notes?: string;
    isCompleted = false;
    categoryId?: number;

    constructor(data?: Partial<IShoppingListItemUpdateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 