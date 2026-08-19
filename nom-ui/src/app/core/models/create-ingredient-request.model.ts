import { NutrientValueInput } from './ingredient-nutrient.model';

export interface CreateIngredientRequest {
  name: string;
  description: string;
  pluralName: string;
  /** Per-100 g facts; omit when none were entered. */
  nutrients?: NutrientValueInput[];
}
