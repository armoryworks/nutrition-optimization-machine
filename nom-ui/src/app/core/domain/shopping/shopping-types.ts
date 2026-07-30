import { RetailPackagingResponse } from '../../models/retail-packaging-response.model';

export interface ShoppingPortion {
  quantity: number;
  unit: string;
}

export interface ShoppingItem {
  ingredientId: number;
  name: string;
  portions: ShoppingPortion[];
  department: string;
  checkKey: string;
  // Raw base quantities for pantry transfer
  baseMassG: number;
  baseVolumeMl: number;
  baseCount: number;
  // Retail package info for quantity override scaling
  retailPackage: RetailPackagingResponse | null;
  retailPackageCount: number;
}

export interface ShoppingDepartment {
  name: string;
  icon: string;
  items: ShoppingItem[];
}
