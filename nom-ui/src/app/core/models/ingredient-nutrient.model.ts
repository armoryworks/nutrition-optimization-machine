/** A stored per-100 g nutrient fact on an ingredient (API: NutrientValueModel). */
export interface IngredientNutrient {
  nutrientId: number;
  nutrientName: string;
  amount: number;
  unitName: string;
}

/** What the ingredient create/update endpoints accept: amount per 100 g in the nutrient's default unit. */
export interface NutrientValueInput {
  nutrientId: number;
  amount: number;
}
