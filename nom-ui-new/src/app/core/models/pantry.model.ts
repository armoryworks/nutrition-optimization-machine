export interface PantryItemResponse {
  id: number;
  householdId: number;
  ingredientId: number;
  ingredientName: string;
  quantity: number;
  measurementId: number;
  measurementName: string;
  measurementSymbol: string;
  itemStatusTypeId: number;
  statusName: string;
  acquisitionDate: string;
  expectedExpirationDate: string | null;
  sourceLocation: string | null;
  notes: string | null;
  isExpired: boolean;
  isExpiringSoon: boolean;
  createdDate: string;
  lastModifiedDate: string | null;
}

export interface PantryItemCreateRequest {
  householdId: number;
  ingredientId: number;
  quantity: number;
  measurementId: number;
  acquisitionDate?: string;
  expectedExpirationDate?: string;
  sourceLocation?: string;
  notes?: string;
}

export interface PantryItemUpdateRequest {
  quantity?: number;
  measurementId?: number;
  expectedExpirationDate?: string;
  itemStatusTypeId?: number;
  sourceLocation?: string;
  notes?: string;
}

export interface ShoppingNeedItem {
  ingredientId: number;
  ingredientName: string;
  quantityNeeded: number;
  quantityOnHand: number;
  quantityToBuy: number;
  measurementId: number;
  measurementName: string;
  measurementSymbol: string;
  measurementCategory: string;
}

export interface ShoppingNeedsResponse {
  householdId: number;
  daysAhead: number;
  fromDate: string;
  toDate: string;
  mealCount: number;
  needs: ShoppingNeedItem[];
}
