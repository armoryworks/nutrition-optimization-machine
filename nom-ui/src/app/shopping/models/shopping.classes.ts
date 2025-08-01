// File: nom-ui/src/app/shopping/models/shopping.classes.ts

import {
    IShoppingListModel,
    IShoppingListCreateRequestModel,
    IShoppingListCreateResponseModel,
    IShoppingListResponseModel,
    IShoppingListItemModel,
    IShoppingListItemCreateRequestModel,
    IShoppingListItemUpdateRequestModel,
    IShoppingListItemResponseModel
} from './shopping.interfaces';

export class ShoppingListModel implements IShoppingListModel {
    id: number = 0;
    householdId: number = 0;
    name: string = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    itemCount: number = 0;
    completedItemCount: number = 0;

    constructor(data?: Partial<IShoppingListModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class ShoppingListCreateRequestModel implements IShoppingListCreateRequestModel {
    householdId: number = 0;
    name: string = '';
    description?: string;

    constructor(data?: Partial<IShoppingListCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class ShoppingListCreateResponseModel implements IShoppingListCreateResponseModel {
    id: number = 0;
    householdId: number = 0;
    name: string = '';
    description?: string;
    createdDate: Date = new Date();
    itemCount: number = 0;
    completedItemCount: number = 0;

    constructor(data?: Partial<IShoppingListCreateResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class ShoppingListResponseModel implements IShoppingListResponseModel {
    id: number = 0;
    householdId: number = 0;
    name: string = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    itemCount: number = 0;
    completedItemCount: number = 0;
    items: IShoppingListItemModel[] = [];

    constructor(data?: Partial<IShoppingListResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class ShoppingListItemModel implements IShoppingListItemModel {
    id: number = 0;
    shoppingListId: number = 0;
    ingredientId?: number;
    ingredientName: string = '';
    quantity: number = 0;
    measurementUnit: string = '';
    notes?: string;
    isCompleted: boolean = false;
    categoryId?: number;
    categoryName?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IShoppingListItemModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class ShoppingListItemCreateRequestModel implements IShoppingListItemCreateRequestModel {
    shoppingListId: number = 0;
    ingredientId?: number;
    ingredientName: string = '';
    quantity: number = 0;
    measurementUnit: string = '';
    notes?: string;
    categoryId?: number;

    constructor(data?: Partial<IShoppingListItemCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class ShoppingListItemUpdateRequestModel implements IShoppingListItemUpdateRequestModel {
    ingredientName: string = '';
    quantity: number = 0;
    measurementUnit: string = '';
    notes?: string;
    isCompleted: boolean = false;
    categoryId?: number;

    constructor(data?: Partial<IShoppingListItemUpdateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class ShoppingListItemResponseModel implements IShoppingListItemResponseModel {
    id: number = 0;
    shoppingListId: number = 0;
    ingredientId?: number;
    ingredientName: string = '';
    quantity: number = 0;
    measurementUnit: string = '';
    notes?: string;
    isCompleted: boolean = false;
    categoryId?: number;
    categoryName?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IShoppingListItemResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 