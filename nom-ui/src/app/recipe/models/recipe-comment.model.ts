export interface RecipeCommentModel {
    id: number;
    recipeId: number;
    text: string;
    createdDate: Date;
    modifiedDate?: Date;
} 