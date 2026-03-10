export interface ShoppingListBulkOperationRequest {
  itemIds: number[];
  operation: 'move' | 'complete' | 'delete';
  targetCategoryId: number | null;
}
