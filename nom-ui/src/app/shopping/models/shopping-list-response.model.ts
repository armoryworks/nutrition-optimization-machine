// File: nom-ui/src/app/shopping/models/shopping-list-response.model.ts

import { IShoppingListResponseModel, IShoppingListItemModel } from './shopping-list-response.model.interface';

export class ShoppingListResponseModel implements IShoppingListResponseModel {
    id: number = 0;
    householdId: number = 0;
    name: string = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    itemCount: number = 0;
    completedItemCount: number = 0;
    completedCount?: number; // Alias for backward compatibility
    items: IShoppingListItemModel[] = [];

    constructor(data?: Partial<IShoppingListResponseModel>) {
        if (data) {
            Object.assign(this, data);
            // Set completedCount as alias for completedItemCount
            if (this.completedItemCount !== undefined && this.completedCount === undefined) {
                this.completedCount = this.completedItemCount;
            }
        }
    }
} 