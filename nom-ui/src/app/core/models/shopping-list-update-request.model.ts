export interface ShoppingListUpdateRequest {
  name: string;
  description: string | null;
  householdId: number | null;
  shoppingListGroupId: number | null;
}
