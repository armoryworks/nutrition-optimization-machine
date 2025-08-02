// File: nom-ui/src/app/shopping/models/shopping-list-create-response.model.interface.ts

export interface IShoppingListCreateResponseModel {
    id: number;
    householdId: number;
    name: string;
    description?: string;
    createdDate: Date;
    itemCount: number;
    completedItemCount: number;
} 