// File: nom-ui/src/app/shopping/models/shopping-list-item.model.ts

import { IShoppingListItemModel } from './shopping-list-item.model.interface';

export class ShoppingListItemModel implements IShoppingListItemModel {
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

    constructor(data?: Partial<IShoppingListItemModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 