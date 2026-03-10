export interface PantryItemUpdateRequest {
  quantity?: number;
  measurementId?: number;
  expectedExpirationDate?: string;
  itemStatusTypeId?: number;
  sourceLocation?: string;
  notes?: string;
}
