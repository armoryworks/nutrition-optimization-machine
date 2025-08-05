// File: nom-ui/src/app/shopping/models/shopping-list-response.model.interface.ts

import { IShoppingListItemModel } from './shopping-list-item.model.interface';

export interface IShoppingListResponseModel {
    id: number;
    householdId: number;
    name: string;
    description?: string;
    createdDate: Date;
    modifiedDate?: Date;
    itemCount: number;
    completedItemCount: number;
    completedCount?: number; // Alias for backward compatibility
    totalItems?: number; // Alias for itemCount
    completedItems?: number; // Alias for completedItemCount
    items: IShoppingListItemModel[];
} 