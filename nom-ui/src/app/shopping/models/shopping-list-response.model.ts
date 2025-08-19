// File: nom-ui/src/app/shopping/models/shopping-list-response.model.ts

import { IShoppingListResponseModel } from './shopping-list-response.model.interface';
import { IShoppingListItemModel } from './shopping-list-item.model.interface';

export class ShoppingListResponseModel implements IShoppingListResponseModel {
    id = 0;
    householdId = 0;
    name = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    itemCount = 0;
    completedItemCount = 0;
    completedCount?: number; // Alias for backward compatibility
    totalItems = 0; // Alias for itemCount
    completedItems = 0; // Alias for completedItemCount
    items: IShoppingListItemModel[] = [];

    constructor(data?: Partial<IShoppingListResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
        // Always set aliases for backward compatibility
        this.completedCount = this.completedItemCount;
        this.totalItems = this.itemCount;
        this.completedItems = this.completedItemCount;
    }
} 