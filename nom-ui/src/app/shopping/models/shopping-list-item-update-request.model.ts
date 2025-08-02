// File: nom-ui/src/app/shopping/models/shopping-list-item-update-request.model.ts

import { IShoppingListItemUpdateRequestModel } from './shopping-list-item-update-request.model.interface';

export class ShoppingListItemUpdateRequestModel implements IShoppingListItemUpdateRequestModel {
    ingredientName: string = '';
    quantity: number = 0;
    measurementUnit: string = '';
    notes?: string;
    isCompleted: boolean = false;
    categoryId?: number;

    constructor(data?: Partial<IShoppingListItemUpdateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 