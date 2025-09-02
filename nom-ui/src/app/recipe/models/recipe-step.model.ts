// File: nom-ui/src/app/recipe/models/recipe-step.model.ts

export interface RecipeStepModel {
    // The step number will be determined by the order in the array on the backend.
    description: string;
    order: number;
}