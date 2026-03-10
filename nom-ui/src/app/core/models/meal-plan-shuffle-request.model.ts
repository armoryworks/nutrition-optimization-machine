export interface MealPlanShuffleRequest {
  householdId: number;
  startDate: string;
  endDate: string;
  replaceExisting: boolean;
}
