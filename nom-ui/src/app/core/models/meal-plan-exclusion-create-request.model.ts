export interface MealPlanExclusionCreateRequest {
  householdId: number;
  personId: number;
  date: string;
  mealTypeId: number | null;
}
