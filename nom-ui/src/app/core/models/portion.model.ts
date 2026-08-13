export interface MealSplit {
  breakfastPct: number;
  lunchPct: number;
  dinnerPct: number;
  snacksPct: number;
}

export interface PortionMember {
  personId: number;
  name: string;
  targetCalories: number;
  targetSource: 'person' | 'household' | 'default';
  sharePct: number;
  plates: number;
  calories: number;
}

export interface PortionRecipe {
  recipeId: number;
  name: string;
  perServingCalories: number;
  recipeServings: number;
  cookFactor: number;
}

export interface PortionBreakdown {
  mealTypeId: number;
  mealType: string;
  budgetCalories: number;
  plateCalories: number;
  totalPlates: number;
  noNutritionData: boolean;
  members: PortionMember[];
  recipes: PortionRecipe[];
}

export interface RangeCookFactor {
  date: string;
  mealTypeId: number;
  recipeId: number;
  cookFactor: number;
}
