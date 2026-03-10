export interface ShoppingListRecipeAddRequest {
  shoppingListId: number;
  recipeId: number;
  includeAllIngredients: boolean;
  selectedIngredientIds: number[] | null;
  scaleFactor: number | null;
}
