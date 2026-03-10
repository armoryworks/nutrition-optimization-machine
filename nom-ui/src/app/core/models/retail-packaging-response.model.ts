export interface RetailPackagingResponse {
  id: number;
  ingredientPattern: string;
  packageName: string;
  packageSize: number;
  packageSizeUnit: string;
  sizeCategory: 'volume' | 'mass' | 'count';
  sizeInBaseUnits: number;
  isDefault: boolean;
  source: string;
}
