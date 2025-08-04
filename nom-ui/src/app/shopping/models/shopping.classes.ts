export class ShoppingListCategory {
    id: number = 0;
    name: string = '';
    description?: string;
    householdId: number = 0;
    sortOrder: number = 0;
    color?: string;
    listCount: number = 0;
}

export class ShoppingListTemplate {
    id: number = 0;
    name: string = '';
    description?: string;
    householdId: number = 0;
    items: ShoppingListItemTemplate[] = [];
    createdDate: Date = new Date();
    modifiedDate?: Date;
}

export class ShoppingListItemTemplate {
    id: number = 0;
    templateId: number = 0;
    name: string = '';
    quantity: number = 1;
    measurementUnit: string = '';
    notes?: string;
    categoryId?: number;
    categoryName?: string;
} 