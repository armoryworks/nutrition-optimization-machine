import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface RecipeModel {
    id: number;
    name: string;
    description: string;
    authorId: number;
    authorName: string;
    rating: number;
    commentCount: number;
    ratingCount: number;
    createdDate: Date;
    modifiedDate?: Date;
    ingredients?: any[];
    steps?: any[];
    isCurated?: boolean;
    curationStatus: string;
}

export interface RecipeCreateModel {
    name: string;
    description: string;
    authorId: number;
}

export interface RecipeUpdateModel {
    name: string;
    description: string;
}

// Recipe Comments
export interface RecipeCommentModel {
    id: number;
    recipeId: number;
    authorId: number;
    authorName: string;
    text: string;
    createdDate: Date;
    modifiedDate?: Date;
}

export interface RecipeCommentCreateModel {
    recipeId: number;
    authorId: number;
    text: string;
}

// Recipe Ratings
export interface RecipeRatingModel {
    id: number;
    recipeId: number;
    authorId: number;
    authorName: string;
    rating: number;
    comment?: string;
    createdDate: Date;
    modifiedDate?: Date;
}

export interface RecipeRatingCreateModel {
    recipeId: number;
    authorId: number;
    rating: number;
    comment?: string;
}

export interface RecipeRatingUpdateModel {
    rating: number;
    comment?: string;
}