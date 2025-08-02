// File: nom-ui/src/app/shopping/models/shopping-list-item-create-request.model.ts

import { IShoppingListItemCreateRequestModel } from './shopping-list-item-create-request.model.interface';

export class ShoppingListItemCreateRequestModel implements IShoppingListItemCreateRequestModel {
    shoppingListId: number = 0;
    ingredientId?: number;
    ingredientName: string = '';
    quantity: number = 0;
    measurementUnit: string = '';
    notes?: string;
    categoryId?: number;

    constructor(data?: Partial<IShoppingListItemCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 