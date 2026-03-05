// ===== Response Models =====

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

export interface ShoppingListDetailResponse extends ShoppingListResponse {
  items: ShoppingListItemResponse[];
}

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

export interface ShoppingListCreateResponse {
  id: number;
  name: string;
  description: string | null;
  authorId: number;
  householdId: number | null;
  shoppingListGroupId: number | null;
  createdDate: string;
}

// ===== Request Models =====

export interface ShoppingListCreateRequest {
  name: string;
  description: string | null;
  householdId: number | null;
  shoppingListGroupId: number | null;
}

export interface ShoppingListUpdateRequest {
  name: string;
  description: string | null;
  householdId: number | null;
  shoppingListGroupId: number | null;
}

export interface ShoppingListItemCreateRequest {
  shoppingListId: number;
  name: string;
  quantity: number | null;
  note: string | null;
  ingredientId: number | null;
  recipeId: number | null;
  position: number | null;
}

export interface ShoppingListItemUpdateRequest {
  name: string;
  quantity: number | null;
  isCompleted: boolean;
  note: string | null;
  position: number | null;
}

export interface ShoppingListCategoryCreateRequest {
  name: string;
  description: string | null;
  sortOrder: number;
  color: string | null;
}

export interface ShoppingListRecipeAddRequest {
  shoppingListId: number;
  recipeId: number;
  includeAllIngredients: boolean;
  selectedIngredientIds: number[] | null;
  scaleFactor: number | null;
}

export interface ShoppingListShareRequest {
  personId: number;
}

export interface ShoppingListBulkOperationRequest {
  itemIds: number[];
  operation: 'move' | 'complete' | 'delete';
  targetCategoryId: number | null;
}
