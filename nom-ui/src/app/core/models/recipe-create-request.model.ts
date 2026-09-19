import { RecipeIngredientRequest } from './recipe-ingredient-request.model';
import { RecipeStepRequest } from './recipe-step-request.model';

export interface RecipeCreateRequest {
  name: string;
  description: string;
  /** Portions the recipe yields; drives the per-serving nutrition label. */
  servings?: number;
  servingQuantity?: number;
  servingQuantityMeasurementId?: number;
  ingredients: RecipeIngredientRequest[];
  steps: RecipeStepRequest[];
}
