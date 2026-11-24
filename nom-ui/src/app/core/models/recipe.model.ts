import { RecipeIngredientModel } from './recipe-ingredient.model';
import { RecipeStepModel } from './recipe-step.model';
import { RecipeAlcoholNote, RecipeNutritionModel, RecipeNutritionSource } from './recipe-nutrition.model';
import { RecipeDishGroupRef } from './dish-group.model';
import { RecipeImageCredit } from './recipe-image.model';

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
  /** Credit for a third-party openly licensed photo; absent for the author's own. */
  imageCredit?: RecipeImageCredit | null;
  prepTimeMinutes?: number;
  cookTimeMinutes?: number;
  servings?: number;
  /** Per-serving amount for the nutrition label (e.g. 252 + "g"). */
  servingQuantity?: number;
  servingUnit?: string;
  servingQuantityMeasurementId?: number;
  rating: number;
  commentCount: number;
  ratingCount: number;
  createdDate: string;
  modifiedDate?: string;
  curationStatus: string;
  /** Where an imported recipe came from, and its credit line ("Recipe by Jane Baker, example.com"). */
  sourceUrl?: string | null;
  sourceAttribution?: string | null;
  /** A third-party credit line found on the source page ("Adapted from ..."), shown with the source link. */
  sourceCredit?: string | null;
  /** True when the recipe comes from a facts-only source: facts and the source link, and a method only in NOM's own words. */
  factsOnly?: boolean;
  /** True while the source's verbatim wording is held for review. */
  containsSourceProse?: boolean;
  /** True while the recipe's own description and method are withheld until they are rewritten; keyFacts and sourceUrl stand in for the method. */
  proseWithheld?: boolean;
  /** The recipe was removed (taken from a forbidden source): only its title, sourceUrl and removedNote are served. */
  removed?: boolean;
  removedNote?: string | null;
  /** Rule-built facts of the source method ("oven 350°F", "bake 30 to 40 min"), in step order, while the method is withheld. */
  keyFacts?: string[] | null;
  /** True when the source's photo is held for review and no NOM image replaces it yet. */
  sourceImageHeld?: boolean;
  /** Visibility tier name ("Private" | "Household" | "Audience" | "Public"), when the API projects it. */
  visibility?: string;
  /** The canonical dish this recipe is a take on, when classified. */
  dishGroup?: RecipeDishGroupRef | null;
  ingredients?: RecipeIngredientModel[];
  steps?: RecipeStepModel[];
  nutrition?: RecipeNutritionModel[];
  /** True when the label was derived from the ingredients rather than hand-authored. */
  nutritionDerived?: boolean;
  /** Where the label's numbers come from; null when there is no label. */
  nutritionSource?: RecipeNutritionSource | null;
  /** Servings the nutrition label is per; null when the recipe never said. */
  nutritionServings?: number | null;
  /** The alcohol note under the label; null when no line carries alcohol. */
  alcohol?: RecipeAlcoholNote | null;
  /** The caller's saved default variation, when one exists. */
  variation?: RecipeVariationItemModel[];
}
