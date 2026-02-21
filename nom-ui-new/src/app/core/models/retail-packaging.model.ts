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

export interface RetailPackagingLookupRequest {
  ingredientNames: string[];
}

export interface RetailPackagingLookupResponse {
  results: RetailPackagingResponse[];
  notFound: string[];
  aiLookupPerformed: boolean;
}

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
