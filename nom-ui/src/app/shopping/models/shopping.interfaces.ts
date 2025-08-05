export interface ShoppingListCreateRequestModel {
    name: string;
    description?: string;
    householdId?: number;
    groupId?: number;
}

export interface ShoppingListCreateResponseModel {
    id: number;
    name: string;
    description?: string;
    householdId?: number;
    groupId?: number;
    createdDate: Date;
    modifiedDate?: Date;
}

export interface ShoppingListItemCreateRequestModel {
    shoppingListId: number;
    ingredientId?: number;
    name: string;
    quantity: number;
    measurementUnit: string;
    notes?: string;
    categoryId?: number;
}

export interface ShoppingListItemUpdateRequestModel {
    id: number;
    shoppingListId: number;
    ingredientId?: number;
    name: string;
    quantity: number;
    measurementUnit: string;
    notes?: string;
    categoryId?: number;
    isCompleted: boolean;
}

export interface ShoppingListItemResponseModel {
    id: number;
    shoppingListId: number;
    ingredientId?: number;
    name: string;
    quantity: number;
    measurementUnit: string;
    notes?: string;
    categoryId?: number;
    categoryName?: string;
    isCompleted: boolean;
    createdDate: Date;
    modifiedDate?: Date;
} 