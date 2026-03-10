import { MealPlanDay } from './meal-plan-day.model';

export interface MealPlanWeekResponse {
  weekStart: string;
  weekEnd: string;
  days: MealPlanDay[];
}
