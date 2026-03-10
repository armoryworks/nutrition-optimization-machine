import { MealRecipeModel } from './meal-recipe.model';

export interface MealModel {
  id: number;
  mealType: string;
  date: string;
  recipes: MealRecipeModel[];
}
