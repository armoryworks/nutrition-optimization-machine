export interface IngredientSubstitutionModel {
  /** The substitute ingredient's id. */
  ingredientId: number;
  name: string;
  /** Pre-computed for this recipe: original quantity × curated ratio. */
  quantity: number;
  measurement?: string;
  measurementId?: number;
  notes?: string;
}

export interface RecipeIngredientModel {
  ingredientId: number;
  name: string;
  quantity: number;
  measurementId: number;
  measurement?: string;
  notes?: string;
  /** Component names of a composite ingredient (label order). */
  subIngredients?: string[];
  /** Curated swap options for this line. */
  substitutions?: IngredientSubstitutionModel[];
}
