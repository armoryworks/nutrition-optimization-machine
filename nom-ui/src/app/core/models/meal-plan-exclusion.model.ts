export interface MealPlanExclusion {
  id: number;
  householdId: number;
  personId: number;
  personName: string;
  date: string;
  mealTypeId: number | null;
  mealType: string | null;
}
