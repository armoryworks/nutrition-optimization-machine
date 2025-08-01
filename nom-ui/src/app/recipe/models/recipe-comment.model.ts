export interface IRecipeCommentModel {
    id: number;
    recipeId: number;
    authorId: number;
    authorName: string;
    commentText: string;
    title?: string;
    createdDate: string;
    lastModifiedDate?: string;
}

export class RecipeCommentModel implements IRecipeCommentModel {
    id: number;
    recipeId: number;
    authorId: number;
    authorName: string;
    commentText: string;
    title?: string;
    createdDate: string;
    lastModifiedDate?: string;

    constructor(data: Partial<IRecipeCommentModel> = {}) {
        this.id = data.id || 0;
        this.recipeId = data.recipeId || 0;
        this.authorId = data.authorId || 0;
        this.authorName = data.authorName || "";
        this.commentText = data.commentText || "";
        this.title = data.title;
        this.createdDate = data.createdDate || "";
        this.lastModifiedDate = data.lastModifiedDate;
    }
}

export interface IRecipeCommentCreateModel {
    recipeId: number;
    commentText: string;
    title?: string;
}

export class RecipeCommentCreateModel implements IRecipeCommentCreateModel {
    recipeId: number;
    commentText: string;
    title?: string;

    constructor(data: Partial<IRecipeCommentCreateModel> = {}) {
        this.recipeId = data.recipeId || 0;
        this.commentText = data.commentText || "";
        this.title = data.title;
    }
} 