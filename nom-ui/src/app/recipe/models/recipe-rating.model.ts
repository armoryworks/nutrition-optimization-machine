export interface RecipeRatingModel {
    id: number;
    recipeId: number;
    rating: number;
    comment?: string;
    createdDate: Date;
    modifiedDate?: Date;
} 