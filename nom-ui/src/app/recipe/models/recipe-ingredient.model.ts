// File: nom-ui/src/app/recipe/models/recipe-ingredient.model.ts

export interface RecipeIngredientModel {
    IngredientId: number;
    quantity: number;
    measurementId: number;
    // The name is included here for display purposes on the frontend,
    // but may not be needed in the final request payload to the backend.
    name?: string;
}