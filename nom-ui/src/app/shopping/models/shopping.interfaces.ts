// File: nom-ui/src/app/shopping/models/shopping.interfaces.ts

export interface IShoppingListModel {
    id: number;
    householdId: number;
    name: string;
    description?: string;
    createdDate: Date;
    modifiedDate?: Date;
    itemCount: number;
    completedItemCount: number;
}

export interface IShoppingListCreateRequestModel {
    householdId: number;
    name: string;
    description?: string;
}

export interface IShoppingListCreateResponseModel {
    id: number;
    householdId: number;
    name: string;
    description?: string;
    createdDate: Date;
    itemCount: number;
    completedItemCount: number;
}

export interface IShoppingListResponseModel {
    id: number;
    householdId: number;
    name: string;
    description?: string;
    createdDate: Date;
    modifiedDate?: Date;
    itemCount: number;
    completedItemCount: number;
    items: IShoppingListItemModel[];
}

export interface IShoppingListItemModel {
    id: number;
    shoppingListId: number;
    ingredientId?: number;
    ingredientName: string;
    quantity: number;
    measurementUnit: string;
    notes?: string;
    isCompleted: boolean;
    categoryId?: number;
    categoryName?: string;
    createdDate: Date;
    modifiedDate?: Date;
}

export interface IShoppingListItemCreateRequestModel {
    shoppingListId: number;
    ingredientId?: number;
    ingredientName: string;
    quantity: number;
    measurementUnit: string;
    notes?: string;
    categoryId?: number;
}

export interface IShoppingListItemUpdateRequestModel {
    ingredientName: string;
    quantity: number;
    measurementUnit: string;
    notes?: string;
    isCompleted: boolean;
    categoryId?: number;
}

export interface IShoppingListItemResponseModel {
    id: number;
    shoppingListId: number;
    ingredientId?: number;
    ingredientName: string;
    quantity: number;
    measurementUnit: string;
    notes?: string;
    isCompleted: boolean;
    categoryId?: number;
    categoryName?: string;
    createdDate: Date;
    modifiedDate?: Date;
} 