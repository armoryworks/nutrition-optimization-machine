// File: nom-ui/src/app/shopping/models/shopping-list-create-response.model.ts

import { IShoppingListCreateResponseModel } from './shopping-list-create-response.model.interface';

export class ShoppingListCreateResponseModel implements IShoppingListCreateResponseModel {
    id = 0;
    householdId = 0;
    name = '';
    description?: string;
    createdDate: Date = new Date();
    itemCount = 0;
    completedItemCount = 0;

    constructor(data?: Partial<IShoppingListCreateResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 