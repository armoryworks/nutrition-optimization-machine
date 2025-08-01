export interface ShoppingListCategory {
    id: number;
    name: string;
    description?: string;
    householdId: number;
    householdName: string;
    sortOrder: number;
    color?: string;
    itemCount: number;
    createdDate: Date;
    lastModifiedDate?: Date;
}

export interface ShoppingListCategoryCreate {
    name: string;
    description?: string;
    sortOrder?: number;
    color?: string;
}

export interface ShoppingListBulkOperation {
    itemIds: number[];
    operation: 'move' | 'complete' | 'delete';
    targetCategoryId?: number;
} 