export interface ShoppingListItemResponse {
  id: number;
  shoppingListId: number;
  name: string;
  quantity: number | null;
  isCompleted: boolean;
  note: string | null;
  ingredientId: number | null;
  recipeId: number | null;
  position: number | null;
  createdDate: string;
  modifiedDate: string | null;
}
