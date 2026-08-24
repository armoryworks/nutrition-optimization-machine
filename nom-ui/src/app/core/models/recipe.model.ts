import { RecipeIngredientModel } from './recipe-ingredient.model';
import { RecipeStepModel } from './recipe-step.model';
import { RecipeNutritionModel } from './recipe-nutrition.model';
import { RecipeDishGroupRef } from './dish-group.model';

export interface RecipeVariationItemModel {
  /** The recipe's original ingredient id being replaced. */
  ingredientId: number;
  substituteIngredientId: number;
  substituteName: string;
  quantity: number;
  measurement?: string;
  measurementId?: number;
}

export interface RecipeDietMatchModel {
  restrictionName: string;
  restrictionType?: string;
  severity?: number;
  ingredientId?: number;
  ingredientName: string;
  /** Why the hit fired, from the category criterion ("high oxalate"). */
  notes?: string;
}

export interface RecipeModel {
  id: number;
  name: string;
  description: string;
  authorName: string;
  authorId: number;
  imageUrl?: string;
  prepTimeMinutes?: number;
  cookTimeMinutes?: number;
  servings?: number;
  /** Per-serving amount for the nutrition label (e.g. 252 + "g"). */
  servingQuantity?: number;
  servingUnit?: string;
  rating: number;
  commentCount: number;
  ratingCount: number;
  createdDate: string;
  modifiedDate?: string;
  curationStatus: string;
  /** Visibility tier name ("Private" | "Household" | "Audience" | "Public"), when the API projects it. */
  visibility?: string;
  /** The canonical dish this recipe is a take on, when classified. */
  dishGroup?: RecipeDishGroupRef | null;
  ingredients?: RecipeIngredientModel[];
  steps?: RecipeStepModel[];
  nutrition?: RecipeNutritionModel[];
  /** The caller's saved default variation, when one exists. */
  variation?: RecipeVariationItemModel[];
}
