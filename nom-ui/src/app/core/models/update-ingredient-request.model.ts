import { NutrientValueInput } from './ingredient-nutrient.model';

export interface UpdateIngredientRequest {
  id: number;
  name: string;
  description: string;
  pluralName: string;
  /** Per-100 g facts. Omit to leave stored nutrition untouched; send [] to clear. */
  nutrients?: NutrientValueInput[];
}
