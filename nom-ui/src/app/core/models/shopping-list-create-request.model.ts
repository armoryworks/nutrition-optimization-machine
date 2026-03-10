export interface ShoppingListCreateRequest {
  name: string;
  description: string | null;
  householdId: number | null;
  shoppingListGroupId: number | null;
}
