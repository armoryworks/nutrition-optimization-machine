export interface IRecipeShareTokenModel {
    id: number;
    recipeId: number;
    recipeName: string;
    shareToken: string;
    shareName?: string;
    isPublic: boolean;
    createdDate: string;
    lastModifiedDate?: string;
}

export class RecipeShareTokenModel implements IRecipeShareTokenModel {
    id: number;
    recipeId: number;
    recipeName: string;
    shareToken: string;
    shareName?: string;
    isPublic: boolean;
    createdDate: string;
    lastModifiedDate?: string;

    constructor(data: Partial<IRecipeShareTokenModel> = {}) {
        this.id = data.id || 0;
        this.recipeId = data.recipeId || 0;
        this.recipeName = data.recipeName || "";
        this.shareToken = data.shareToken || "";
        this.shareName = data.shareName;
        this.isPublic = data.isPublic || false;
        this.createdDate = data.createdDate || "";
        this.lastModifiedDate = data.lastModifiedDate;
    }
}

export interface IRecipeShareTokenCreateModel {
    recipeId: number;
    shareName?: string;
    isPublic: boolean;
}

export class RecipeShareTokenCreateModel implements IRecipeShareTokenCreateModel {
    recipeId: number;
    shareName?: string;
    isPublic: boolean;

    constructor(data: Partial<IRecipeShareTokenCreateModel> = {}) {
        this.recipeId = data.recipeId || 0;
        this.shareName = data.shareName;
        this.isPublic = data.isPublic || false;
    }
} 