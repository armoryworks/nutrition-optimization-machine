import { MealPlanCell } from './meal-plan-cell.model';
import { MealPlanExclusion } from './meal-plan-exclusion.model';

export interface MealPlanDay {
  date: string;
  dayOfWeek: string;
  cells: MealPlanCell[];
  exclusions: MealPlanExclusion[];
}
