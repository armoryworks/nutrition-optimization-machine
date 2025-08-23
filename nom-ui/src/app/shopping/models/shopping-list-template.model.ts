import { ShoppingListItemTemplate } from './shopping-list-item-template.model';

export class ShoppingListTemplate {
    id = 0;
    name = '';
    description?: string;
    householdId = 0;
    items: ShoppingListItemTemplate[] = [];
    createdDate: Date = new Date();
    modifiedDate?: Date;
}
