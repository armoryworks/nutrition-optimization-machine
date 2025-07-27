// File: nom-ui/src/app/recipe/models/update-recipe-request.model.ts

import { RecipeIngredientModel } from './recipe-ingredient.model';
import { RecipeStepModel } from './recipe-step.model';



export interface UpdateRecipeRequest {
    id: number;
    name: string;
    description?: string;
    ingredients: RecipeIngredientModel[];
    steps: RecipeStepModel[];
    // Include any other fields that can be updated,
    // e.g., prepTimeMinutes, cookTimeMinutes, servings
}