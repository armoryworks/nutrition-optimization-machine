/**
 * Hardcoded meal composition templates for the shuffle feature.
 * Defines how many recipes of each type compose a meal.
 *
 * Recipe Type IDs (reference.Reference, group 3):
 *   3100 = Appetizer/Starter
 *   3101 = Entree
 *   3102 = Starch/Carbohydrate
 *   3103 = Vegetable/Salad
 *   3104 = Snack
 *   3105 = Dessert
 *
 * Meal Type IDs (reference.Reference, group 1):
 *   1100 = Breakfast
 *   1101 = Lunch
 *   1102 = Dinner
 *   1103 = Snacks
 */

export interface MealCompositionSlot {
  recipeTypeId: number;
  label: string;
}

export const RECIPE_TYPE_IDS = {
  APPETIZER: 3100,
  ENTREE: 3101,
  STARCH: 3102,
  VEGETABLE: 3103,
  SNACK: 3104,
  DESSERT: 3105,
} as const;

export const MEAL_TYPE_IDS = {
  BREAKFAST: 1100,
  LUNCH: 1101,
  DINNER: 1102,
  SNACKS: 1103,
} as const;

const DEFAULT_MEAL_COMPOSITION: Record<number, MealCompositionSlot[]> = {
  [MEAL_TYPE_IDS.BREAKFAST]: [
    { recipeTypeId: RECIPE_TYPE_IDS.ENTREE, label: 'Entree' },
    { recipeTypeId: RECIPE_TYPE_IDS.VEGETABLE, label: 'Fruit/Vegetable' },
  ],
  [MEAL_TYPE_IDS.LUNCH]: [
    { recipeTypeId: RECIPE_TYPE_IDS.APPETIZER, label: 'Appetizer' },
    { recipeTypeId: RECIPE_TYPE_IDS.ENTREE, label: 'Entree' },
    { recipeTypeId: RECIPE_TYPE_IDS.STARCH, label: 'Starch' },
  ],
  [MEAL_TYPE_IDS.DINNER]: [
    { recipeTypeId: RECIPE_TYPE_IDS.APPETIZER, label: 'Appetizer' },
    { recipeTypeId: RECIPE_TYPE_IDS.ENTREE, label: 'Entree' },
    { recipeTypeId: RECIPE_TYPE_IDS.STARCH, label: 'Starch' },
  ],
  [MEAL_TYPE_IDS.SNACKS]: [
    { recipeTypeId: RECIPE_TYPE_IDS.SNACK, label: 'Snack' },
  ],
};

/** Returns the composition slots for a meal type, or a single untyped slot as fallback. */
export function getCompositionForMealType(mealTypeId: number): MealCompositionSlot[] {
  return DEFAULT_MEAL_COMPOSITION[mealTypeId] ?? [{ recipeTypeId: 0, label: 'Any' }];
}
