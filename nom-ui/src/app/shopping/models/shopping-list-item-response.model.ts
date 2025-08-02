// File: nom-ui/src/app/shopping/models/shopping-list-item-response.model.ts

import { IShoppingListItemResponseModel } from './shopping-list-item-response.model.interface';

export class ShoppingListItemResponseModel implements IShoppingListItemResponseModel {
    id: number = 0;
    shoppingListId: number = 0;
    ingredientId?: number;
    ingredientName: string = '';
    quantity: number = 0;
    measurementUnit: string = '';
    notes?: string;
    isCompleted: boolean = false;
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