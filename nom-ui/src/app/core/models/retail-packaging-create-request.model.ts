export interface RetailPackagingCreateRequest {
  ingredientPattern: string;
  packageName: string;
  packageSize: number;
  packageSizeUnit: string;
  sizeCategory: string;
  sizeInBaseUnits?: number;
  isDefault?: boolean;
  source?: string;
}
