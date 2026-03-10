export interface ShoppingListItemCreateRequest {
  shoppingListId: number;
  name: string;
  quantity: number | null;
  note: string | null;
  ingredientId: number | null;
  recipeId: number | null;
  position: number | null;
}
