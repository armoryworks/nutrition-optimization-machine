export interface MealPlanWeekResponse {
  weekStart: string;
  weekEnd: string;
  days: MealPlanDay[];
}

export interface MealPlanDay {
  date: string;
  dayOfWeek: string;
  cells: MealPlanCell[];
  exclusions: MealPlanExclusion[];
}

export interface MealPlanCell {
  mealTypeId: number;
  mealType: string;
  entries: MealPlanEntry[];
  totalCalories: number | null;
  totalProteinGrams: number | null;
  totalCarbGrams: number | null;
  totalFatGrams: number | null;
}

export interface MealPlanEntry {
  id: number;
  recipeId: number | null;
  recipeName: string | null;
  recipeImage: string | null;
  title: string | null;
  notes: string | null;
  calories: number | null;
  proteinGrams: number | null;
  carbGrams: number | null;
  fatGrams: number | null;
  completedDate: string | null;
}

export interface MealPlanExclusion {
  id: number;
  householdId: number;
  personId: number;
  personName: string;
  date: string;
  mealTypeId: number | null;
  mealType: string | null;
}

export interface MealPlanCreateRequest {
  householdId: number;
  date: string;
  mealTypeId: number;
  title: string | null;
  notes: string | null;
  recipeId: number | null;
}

export interface MealPlanUpdateRequest {
  date: string;
  mealTypeId: number;
  title: string | null;
  notes: string | null;
  recipeId: number | null;
}

export interface MealPlanResponse {
  id: number;
  householdId: number;
  authorId: number;
  date: string;
  mealTypeId: number;
  mealType: string;
  title: string | null;
  notes: string | null;
  recipeId: number | null;
  recipeName: string | null;
  createdDate: string;
  modifiedDate: string | null;
}

export interface MealPlanExclusionCreateRequest {
  householdId: number;
  personId: number;
  date: string;
  mealTypeId: number | null;
}

export interface MealPlanShuffleRequest {
  householdId: number;
  startDate: string;
  endDate: string;
  replaceExisting: boolean;
}

export interface MealPlanShuffleResponse {
  created: number;
  deleted: number;
  week: MealPlanWeekResponse;
}
