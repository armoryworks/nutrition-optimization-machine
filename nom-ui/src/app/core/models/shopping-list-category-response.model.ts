export interface ShoppingListCategoryResponse {
  id: number;
  name: string;
  description: string | null;
  householdId: number;
  householdName: string;
  sortOrder: number;
  color: string | null;
  itemCount: number;
  createdDate: string;
  lastModifiedDate: string | null;
}
