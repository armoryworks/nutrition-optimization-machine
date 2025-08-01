export interface IRecipeRatingModel {
    id: number;
    recipeId: number;
    authorId: number;
    authorName: string;
    rating: number;
    reviewText?: string;
    createdDate: string;
    lastModifiedDate?: string;
}

export class RecipeRatingModel implements IRecipeRatingModel {
    id: number;
    recipeId: number;
    authorId: number;
    authorName: string;
    rating: number;
    reviewText?: string;
    createdDate: string;
    lastModifiedDate?: string;

    constructor(data: Partial<IRecipeRatingModel> = {}) {
        this.id = data.id || 0;
        this.recipeId = data.recipeId || 0;
        this.authorId = data.authorId || 0;
        this.authorName = data.authorName || "";
        this.rating = data.rating || 0;
        this.reviewText = data.reviewText;
        this.createdDate = data.createdDate || "";
        this.lastModifiedDate = data.lastModifiedDate;
    }
}

export interface IRecipeRatingCreateModel {
    recipeId: number;
    rating: number;
    reviewText?: string;
}

export class RecipeRatingCreateModel implements IRecipeRatingCreateModel {
    recipeId: number;
    rating: number;
    reviewText?: string;

    constructor(data: Partial<IRecipeRatingCreateModel> = {}) {
        this.recipeId = data.recipeId || 0;
        this.rating = data.rating || 0;
        this.reviewText = data.reviewText;
    }
} 