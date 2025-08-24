import { IngredientModel } from './ingredient.model';
import { RecipeStepModel } from './recipe-step.model';

export interface RecipeModel {
    id: number;
    name: string;
    description: string;
    authorName: string;
    rating: number;
    commentCount: number;
    ratingCount: number;
    createdDate: Date;
    modifiedDate?: Date;
    ingredients?: IngredientModel[];
    steps?: RecipeStepModel[];
    isCurated?: boolean;
    curationStatus: string;
    authorId: number;
    createdById: number;
    userId: number;
}