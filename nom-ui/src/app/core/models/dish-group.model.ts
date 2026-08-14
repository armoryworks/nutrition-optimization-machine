/** A canonical dish group ("chocolate chip cookies") with its visible-member count. */
export interface DishGroupModel {
  id: number;
  name: string;
  slug: string;
  recipeCount: number;
}

/** One member of a dish group, shaped for rails and grids. */
export interface DishGroupRecipeModel {
  id: number;
  name: string;
  image: string | null;
  rating: number | null;
}

export interface DishGroupDetailModel extends DishGroupModel {
  recipes: DishGroupRecipeModel[];
}

/** Compact dish-group reference carried on the recipe detail response. */
export interface RecipeDishGroupRef {
  id: number;
  name: string;
  slug: string;
}
