export interface ShoppingListResponse {
  id: number;
  name: string;
  description: string | null;
  authorId: number;
  householdId: number | null;
  shoppingListGroupId: number | null;
  itemCount: number;
  completedItemCount: number;
  createdDate: string;
  modifiedDate: string | null;
}
