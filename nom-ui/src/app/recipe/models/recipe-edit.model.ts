// File: nom-ui/src/app/recipe/models/recipe-edit.model.ts

export interface RecipeEditModel {
    id: number;
    name: string;
    description?: string;
    authorId: number;
    ingredients: RecipeIngredientModel[];
    steps: RecipeStepModel[];
}

export interface RecipeIngredientModel {
    ingredientId: number;
    name: string;
    quantity: number;
    measurementTypeId: number;
}

export interface RecipeStepModel {
    id: number;
    description: string;
    order: number;
} 