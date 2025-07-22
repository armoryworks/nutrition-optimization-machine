// File: nom-ui/src/app/recipe/models/ingredient.model.ts
import { NutrientValueModel } from './nutrient-value.model';

export interface IngredientModel {
  id: number;
  name: string;
  fdcId: string;
  description: string;
  nutrients: NutrientValueModel[];
}