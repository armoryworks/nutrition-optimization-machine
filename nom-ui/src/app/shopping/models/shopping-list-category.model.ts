import { IShoppingListCategoryModel } from './shopping-list-category.model.interface';

export class ShoppingListCategory implements IShoppingListCategoryModel {
    id: number = 0;
    householdId: number = 0;
    name: string = '';
    description?: string;
    sortOrder: number = 0;
    color?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    listCount: number = 0; // Number of lists in this category
    itemCount: number = 0; // Alias for listCount

    constructor(data?: Partial<IShoppingListCategoryModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class ShoppingListCategoryCreate {
    householdId: number = 0;
    name: string = '';
    description?: string;
    sortOrder: number = 0;
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