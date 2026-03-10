import { MealPlanWeekResponse } from './meal-plan-week-response.model';

export interface MealPlanShuffleResponse {
  created: number;
  deleted: number;
  week: MealPlanWeekResponse;
}
