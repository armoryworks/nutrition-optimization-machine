// File: nom-ui/src/app/recipe/models/recipe-edit.model.ts

import { RecipeIngredientModel } from './recipe-ingredient.model';
import { RecipeStepModel } from './recipe-step.model';

export interface RecipeEditModel {
    id: number;
    name: string;
    description?: string;
    ingredients: RecipeIngredientModel[];
    steps: RecipeStepModel[];
} 