import { RecipeIngredientRequest } from './recipe-ingredient-request.model';
import { RecipeStepRequest } from './recipe-step-request.model';

export interface RecipeUpdateRequest {
  id: number;
  name: string;
  description?: string;
  ingredients: RecipeIngredientRequest[];
  steps: RecipeStepRequest[];
}
