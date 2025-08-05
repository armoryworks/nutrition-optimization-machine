export interface IShoppingListCategoryModel {
    id: number;
    householdId: number;
    name: string;
    description?: string;
    sortOrder: number;
    color?: string;
    createdDate: Date;
    modifiedDate?: Date;
    listCount: number;
} 