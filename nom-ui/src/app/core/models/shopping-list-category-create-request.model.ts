export interface ShoppingListCategoryCreateRequest {
  name: string;
  description: string | null;
  sortOrder: number;
  color: string | null;
}
