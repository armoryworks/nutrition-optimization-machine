// File: nom-ui/src/app/shopping/models/shopping-list.model.ts

import { IShoppingListModel } from './shopping-list.model.interface';

export class ShoppingListModel implements IShoppingListModel {
    id: number = 0;
    householdId: number = 0;
    name: string = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    itemCount: number = 0;
    completedItemCount: number = 0;

    constructor(data?: Partial<IShoppingListModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 