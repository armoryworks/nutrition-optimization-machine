import { RecipeDietMatchModel } from './recipe.model';

export interface MealPlanEntry {
  id: number;
  recipeId: number | null;
  recipeName: string | null;
  recipeImage: string | null;
  /** The recipe was removed (taken from a forbidden source): the entry keeps its title, the source link and a plain note. */
  recipeRemoved?: boolean;
  recipeSourceUrl?: string | null;
  recipeRemovedNote?: string | null;
  /** Set when this slot holds a standalone whole food (apple, protein bar) instead of a recipe. */
  ingredientId?: number | null;
  ingredientName?: string | null;
  quantity?: number | null;
  measurementId?: number | null;
  measurementName?: string | null;
  foodGroupId?: number | null;
  foodGroupName?: string | null;
  /** Set when the standalone food is a prepared food (fast food, restaurant or baby food, a meal or entree). */
  isPrepared?: boolean;
  /** For a prepared food: how many to buy for this slot, its quantity for each member who eats the meal. */
  servingsToBuy?: number | null;
  /** Grams in one reference serving; null means the shown nutrition uses the 100 g basis. */
  referenceServingGrams?: number | null;
  title: string | null;
  notes: string | null;
  calories: number | null;
  proteinGrams: number | null;
  carbGrams: number | null;
  fatGrams: number | null;
  completedDate: string | null;
  shoppingCompletedAt: string | null;
  /** The viewer's severity ≥ 4 restrictions this entry's recipe trips. */
  dietConflicts?: RecipeDietMatchModel[];
}
