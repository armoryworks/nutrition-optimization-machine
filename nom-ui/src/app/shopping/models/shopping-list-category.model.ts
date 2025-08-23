import { IShoppingListCategoryModel } from './shopping-list-category.model.interface';

export class ShoppingListCategory {
    id = 0;
    name = '';
    description?: string;
    householdId = 0;
    sortOrder = 0;
    color?: string;
    listCount = 0;
    itemCount = 0; // Alias for listCount for backward compatibility
}

export class ShoppingListCategoryCreate {
    householdId = 0;
    name = '';
    description?: string;
    sortOrder = 0;
    color?: string;

    constructor(data?: Partial<ShoppingListCategoryCreate>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export interface ShoppingListBulkOperation {
    itemIds: number[];
    operation: 'move' | 'complete' | 'delete';
    targetCategoryId?: number;
} 