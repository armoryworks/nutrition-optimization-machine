// File: nom-ui/src/app/shopping/models/shopping-list-create-request.model.ts

import { IShoppingListCreateRequestModel } from './shopping-list-create-request.model.interface';

export class ShoppingListCreateRequestModel implements IShoppingListCreateRequestModel {
    householdId = 0;
    name = '';
    description?: string;

    constructor(data?: Partial<IShoppingListCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 