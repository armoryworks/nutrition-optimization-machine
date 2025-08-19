export class ShoppingListCategory {
    id = 0;
    name = '';
    description?: string;
    householdId = 0;
    sortOrder = 0;
    color?: string;
    listCount = 0;
}

export class ShoppingListTemplate {
    id = 0;
    name = '';
    description?: string;
    householdId = 0;
    items: ShoppingListItemTemplate[] = [];
    createdDate: Date = new Date();
    modifiedDate?: Date;
}

export class ShoppingListItemTemplate {
    id = 0;
    templateId = 0;
    name = '';
    quantity = 1;
    measurementUnit = '';
    notes?: string;
    categoryId?: number;
    categoryName?: string;
}

// Re-export the ShoppingListResponseModel from its dedicated file
export { ShoppingListResponseModel } from './shopping-list-response.model'; 