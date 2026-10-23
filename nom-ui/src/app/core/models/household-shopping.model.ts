/**
 * Household shopping contracts — see `ShoppingController` (`/api/Shopping`).
 *
 * Ad-hoc items live on the server per household; plan/pantry-derived lines are
 * still computed in the browser and only their check state (`itemKey`) is
 * shared. Verification rows record what a household member scanned or typed
 * about a food; nothing here ever writes to the shared nutrient catalog.
 */

export interface HouseholdShoppingItem {
  id: number;
  householdId: number;
  name: string;
  ingredientId?: number | null;
  quantity?: number | null;
  unit?: string | null;
  checked: boolean;
  checkedDate?: string | null;
  checkedByPersonId?: number | null;
  createdByPersonId?: number | null;
  createdDate: string;
  /** The linked ingredient's food-group label, when known. */
  department?: string | null;
  ingredientNeedsVerification: boolean;
}

/** `linkToCatalog: false` keeps a non-food (paper towels, dish soap) as a plain line with no catalog food behind it. */
export interface HouseholdShoppingItemCreate {
  householdId: number;
  name: string;
  ingredientId?: number | null;
  quantity?: number | null;
  unit?: string | null;
  linkToCatalog?: boolean;
}

/** `created` is false when the name was already on the list; the server unticks that line instead of adding a twin. */
export interface HouseholdShoppingItemAdded {
  item: HouseholdShoppingItem;
  created: boolean;
}

export interface HouseholdShoppingItemUpdate {
  name?: string;
  quantity?: number | null;
  unit?: string | null;
  ingredientId?: number | null;
}

export interface ShoppingCheck {
  itemKey: string;
  checkedDate: string;
  checkedByPersonId?: number | null;
}

export interface ShoppingState {
  items: HouseholdShoppingItem[];
  checks: ShoppingCheck[];
}

/** Exactly one of `itemKey` (derived line) or `itemId` (ad-hoc item). */
export interface ShoppingCheckRequest {
  householdId: number;
  itemKey?: string;
  itemId?: number;
  checked: boolean;
}

export interface ShoppingCheckResult {
  itemKey: string;
  itemId?: number | null;
  checked: boolean;
  checkedDate?: string | null;
  /** Present only when the tick resolves to a food the household should be asked about. */
  verification?: NeedsVerification | null;
}

export interface NeedsVerification {
  ingredientId: number;
  name: string;
  hasVerifiedNutrition: boolean;
  hasUpc: boolean;
  askedRecently: boolean;
  shouldAsk: boolean;
}

export type LabelNutrientUnit = 'g' | 'mg' | 'mcg';

export interface NutrientReading {
  key: string;
  amount?: number | null;
  unit: LabelNutrientUnit;
  lessThan?: number | null;
  percentDailyValue?: number | null;
  raw: string;
}

export interface Per100gReading {
  calories?: number | null;
  nutrients: { key: string; amount: number; unit: LabelNutrientUnit }[];
}

/** Values are per serving exactly as printed unless `basis` says per 100 g. */
export interface NutritionFactsParseResult {
  parsed: boolean;
  basis: 'serving' | 'per100g' | 'unknown';
  servingText?: string | null;
  servingGrams?: number | null;
  servingMl?: number | null;
  servingsPerContainer?: number | null;
  servingsPerContainerApprox: boolean;
  dualColumn: boolean;
  secondColumnLabel?: string | null;
  calories?: number | null;
  nutrients: NutrientReading[];
  notSignificantSource: string[];
  per100g?: Per100gReading | null;
  warnings: string[];
  fieldsRecognised: number;
  confidence: number;
  matchedLines: string[];
}

export interface LabelScanResult {
  verificationId: number;
  parsed: NutritionFactsParseResult;
  ocrText: string;
  imageStored: boolean;
}

export interface LabelNutrientConfirm {
  key: string;
  amount?: number | null;
  unit: LabelNutrientUnit;
  percentDailyValue?: number | null;
}

/** `basis` follows the scan: a per-100 g table is saved as printed and never scaled by a serving weight. */
export interface IngredientVerificationConfirm {
  basis: 'serving' | 'per100g';
  servingText?: string | null;
  servingGrams?: number | null;
  servingMl?: number | null;
  servingsPerContainer?: number | null;
  calories?: number | null;
  nutrients: LabelNutrientConfirm[];
}

export interface IngredientVerification {
  id: number;
  ingredientId: number;
  householdId?: number | null;
  kind: string;
  status: string;
  source: string;
  upc?: string | null;
  servingText?: string | null;
  servingGrams?: number | null;
  servingMl?: number | null;
  servingsPerContainer?: number | null;
  packageGrams?: number | null;
  packageMl?: number | null;
  piecesPerPackage?: number | null;
  cupGrams?: number | null;
  nutrients?: NutritionFactsParseResult | null;
  confirmed: boolean;
  warnings: string[];
  lastAskedDate: string;
  createdDate: string;
}

export interface BarcodeResult {
  verificationId?: number | null;
  upc?: string | null;
  decoded: boolean;
  source: 'client' | 'server';
  format?: string | null;
  appliedToIngredient: boolean;
}

export type PackageUnit = 'g' | 'kg' | 'oz' | 'lb' | 'ml' | 'l' | 'floz';

export interface IngredientMeasurements {
  householdId: number;
  packageAmount?: number | null;
  packageUnit?: PackageUnit | null;
  piecesPerPackage?: number | null;
  cupGrams?: number | null;
}

export interface NotFoodResult {
  ingredientId: number;
  itemsUnlinked: number;
  ingredientRetired: boolean;
}

export type VerificationFlowResult =
  | { action: 'label'; scan: LabelScanResult }
  | { action: 'barcode'; result: BarcodeResult }
  | { action: 'skip' }
  | { action: 'not-food'; result: NotFoodResult };

/** What the sheets ended with; the shopping page only needs to react to `not-food`. */
export type VerificationFlowOutcome = 'skipped' | 'not-food' | 'recorded' | 'dismissed';
