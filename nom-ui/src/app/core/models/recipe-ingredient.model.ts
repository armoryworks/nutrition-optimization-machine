import { RecipeSubstitutionStepEffectModel } from './recipe-substitution.model';

export interface IngredientSubstitutionModel {
  /** The substitute ingredient's id. */
  ingredientId: number;
  name: string;
  /** Pre-computed for this recipe: original quantity × curated ratio. */
  quantity: number;
  measurement?: string;
  measurementId?: number;
  notes?: string;
  /** The substitute's mass at this quantity, when known. */
  grams?: number | null;
  /** Step alterations carried by a recipe-scoped substitution, when it has any. */
  stepEffects?: RecipeSubstitutionStepEffectModel[];
}

export interface RecipeIngredientModel {
  ingredientId: number;
  name: string;
  /** The ingredient's nutritional food group, when classified; the shopping list's aisle fallback. */
  foodGroupName?: string | null;
  quantity: number;
  measurementId: number;
  measurement?: string;
  notes?: string;
  /** Component names of a composite ingredient (label order). */
  subIngredients?: string[];
  /** Curated swap options for this line. */
  substitutions?: IngredientSubstitutionModel[];
  /** The line's mass in grams, when known. */
  grams?: number | null;
  /** Whether the line counts toward the derived nutrition label: counted, counted-assumed, no-data, no-conversion, not-measured or not-an-ingredient. */
  nutritionStatus?: string;
  /** For a counted-assumed line, the container size it is weighed at and where that size comes from. */
  assumedSize?: string | null;
}
