// File: nom-ui/src/app/shopping/models/shopping-list-create-response.model.ts

import { IShoppingListCreateResponseModel } from './shopping-list-create-response.model.interface';

export class ShoppingListCreateResponseModel implements IShoppingListCreateResponseModel {
    id: number = 0;
    householdId: number = 0;
    name: string = '';
    description?: string;
    createdDate: Date = new Date();
    itemCount: number = 0;
    completedItemCount: number = 0;

    constructor(data?: Partial<IShoppingListCreateResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 