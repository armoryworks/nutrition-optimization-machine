import { RetailPackagingResponse } from '../../models/retail-packaging-response.model';

export interface ShoppingPortion {
  quantity: number;
  unit: string;
}

export type ShoppingItemSource = 'plan' | 'adhoc';

export interface ShoppingItem {
  /** Null only for a household-added item the server could not link to a food. */
  ingredientId: number | null;
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
  /** Planned recipes this line derives from ("why is this on my list"). */
  sourceRecipes: { id: number; name: string }[];
  /** Derived from the meal plan, or typed in by a household member. */
  source: ShoppingItemSource;
  /** The server row behind an ad-hoc line. */
  adhocId?: number;
}

export interface ShoppingDepartment {
  name: string;
  icon: string;
  items: ShoppingItem[];
}
