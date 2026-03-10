export interface ShoppingListItemUpdateRequest {
  name: string;
  quantity: number | null;
  isCompleted: boolean;
  note: string | null;
  position: number | null;
}
