// File: nom-ui/src/app/shopping/models/shopping-list.model.interface.ts

export interface IShoppingListModel {
    id: number;
    householdId: number;
    name: string;
    description?: string;
    createdDate: Date;
    modifiedDate?: Date;
    itemCount: number;
    completedItemCount: number;
    authorId: number;
    createdById: number;
    userId: number;
} 