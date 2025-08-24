// File: nom-ui/src/app/shopping/models/shopping-list.model.ts

import { IShoppingListModel } from './shopping-list.model.interface';

export class ShoppingListModel implements IShoppingListModel {
    id = 0;
    householdId = 0;
    name = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    itemCount = 0;
    completedItemCount = 0;
    authorId = 0;
    createdById = 0;
    userId = 0;

    constructor(data?: Partial<IShoppingListModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 