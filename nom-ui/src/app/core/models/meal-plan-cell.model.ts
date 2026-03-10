import { MealPlanEntry } from './meal-plan-entry.model';

export interface MealPlanCell {
  mealTypeId: number;
  mealType: string;
  entries: MealPlanEntry[];
  totalCalories: number | null;
  totalProteinGrams: number | null;
  totalCarbGrams: number | null;
  totalFatGrams: number | null;
}
