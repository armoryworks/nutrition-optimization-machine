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

export class RecipeRatingModel {
    id = 0;
    recipeId = 0;
    rating = 0;
    comment = '';
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<RecipeRatingModel>) {
        if (data) {
            Object.assign(this, data);
        }
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

export interface IRecipeRatingUpdateModel {
    rating: number;
    reviewText?: string;
}

export class RecipeRatingUpdateModel implements IRecipeRatingUpdateModel {
    rating: number;
    reviewText?: string;

    constructor(data: Partial<IRecipeRatingUpdateModel> = {}) {
        this.rating = data.rating || 0;
        this.reviewText = data.reviewText;
    }
} 