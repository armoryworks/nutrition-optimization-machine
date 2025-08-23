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

export class RecipeCommentModel {
    id = 0;
    recipeId = 0;
    content = '';
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<RecipeCommentModel>) {
        if (data) {
            Object.assign(this, data);
        }
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