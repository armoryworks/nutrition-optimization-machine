export interface ShoppingListCreateResponse {
  id: number;
  name: string;
  description: string | null;
  authorId: number;
  householdId: number | null;
  shoppingListGroupId: number | null;
  createdDate: string;
}
