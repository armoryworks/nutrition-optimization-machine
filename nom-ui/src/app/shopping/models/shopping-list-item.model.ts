// File: nom-ui/src/app/shopping/models/shopping-list-item.model.ts

import { IShoppingListItemModel } from './shopping-list-item.model.interface';

export class ShoppingListItemModel implements IShoppingListItemModel {
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

    constructor(data?: Partial<IShoppingListItemModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 