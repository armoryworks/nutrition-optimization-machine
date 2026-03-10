import { GoalModel } from './goal.model';
import { MealModel } from './meal.model';
import { PlanRestrictionModel } from './plan-restriction.model';

export interface CreatePlanRequest {
  name: string;
  description: string | null;
  startDate: string;
  endDate: string | null;
  goals: GoalModel[];
  meals: MealModel[];
  restrictions: PlanRestrictionModel[];
}
