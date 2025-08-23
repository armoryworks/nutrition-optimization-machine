// File: nom-ui/src/app/recipe/models/recipe-edit.model.ts

export interface RecipeEditModel {
    id: number;
    name: string;
    description?: string;
    ingredients: RecipeIngredientModel[];
    steps: RecipeStepModel[];
}

export interface RecipeIngredientModel {
    ingredientId: number;
    name: string;
    quantity: number;
    measurementId: number;
}

export interface RecipeStepModel {
    id: number;
    description: string;
    order: number;
} 