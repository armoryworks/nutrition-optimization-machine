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
