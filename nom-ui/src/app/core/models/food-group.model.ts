export interface FoodGroup {
  id: number;
  name: string;
  description: string | null;
}

export type FoodGroupTimeframe = 'PerDay' | 'PerMeal';

export interface FoodGroupRule {
  id: number;
  householdId: number;
  foodGroupId: number;
  foodGroupName: string | null;
  minServings: number;
  timeframe: FoodGroupTimeframe;
  mealTypeId: number | null;
  mealTypeName: string | null;
  isActive: boolean;
}

export interface FoodGroupRuleUpsert {
  householdId: number;
  foodGroupId: number;
  minServings: number;
  timeframe: FoodGroupTimeframe;
  mealTypeId: number | null;
  isActive: boolean;
}
