import { IShoppingListCategoryModel } from './shopping-list-category.model.interface';

export class ShoppingListCategory implements IShoppingListCategoryModel {
    id = 0;
    householdId = 0;
    name = '';
    description?: string;
    sortOrder = 0;
    color?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    listCount = 0; // Number of lists in this category
    itemCount = 0; // Alias for listCount

    constructor(data?: Partial<IShoppingListCategoryModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
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