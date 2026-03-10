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
