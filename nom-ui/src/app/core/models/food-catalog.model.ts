export interface FoodCatalogItem {
  id: number;
  name: string;
  fdcId: string | null;
  source: string | null;
  curationStatusId: number;
  curationStatus: string | null;
  foodGroupId: number | null;
  foodGroupName: string | null;
  isWholeFood: boolean | null;
  referenceServingGrams: number | null;
  caloriesPer100g: number | null;
  proteinPer100g: number | null;
  carbPer100g: number | null;
  fatPer100g: number | null;
  flags: string[];
}

export interface FoodCatalogPage {
  total: number;
  items: FoodCatalogItem[];
}

export interface FoodCatalogUpdate {
  name?: string | null;
  foodGroupId?: number | null;
  isWholeFood?: boolean | null;
  referenceServingGrams?: number | null;
  curationStatusId?: number | null;
}

export interface FoodCatalogFinding {
  ingredientId: number;
  name: string;
  fdcId: string | null;
  source: string | null;
  code: string;
  severity: 'high' | 'medium' | 'low';
  detail: string;
}

export interface FoodCatalogAuditResult {
  examined: number;
  findings: FoodCatalogFinding[];
}

export interface FoodProposal {
  id: number;
  action: string;
  ingredientId: number | null;
  ingredientName: string | null;
  fdcId: string | null;
  field: string | null;
  currentValue: string | null;
  proposedValue: string | null;
  confidence: number | null;
  reason: string | null;
  source: string;
  batch: string | null;
  status: string;
}

/** Curation status ids from the reference seed. */
export const CurationStatus = {
  NonCurated: 9000,
  PendingCuration: 9001,
  RequiresRevision: 9002,
  Curated: 9003,
  Rejected: 9004,
} as const;
