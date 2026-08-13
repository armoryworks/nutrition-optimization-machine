export interface RestrictionCategoryModel {
  id: number;
  name: string;
  description?: string;
  criteriaCount: number;
}

export interface RestrictionGroupModel {
  id: number;
  name: string;
  description?: string;
  categories: RestrictionCategoryModel[];
}

export interface RestrictionCriterionModel {
  id: number;
  restrictionTypeId: number;
  ingredientId?: number;
  ingredientName?: string;
  ingredientPattern?: string;
  nutrientId?: number;
  nutrientName?: string;
  maxAmountPerServing?: number;
  severity: number;
  notes?: string;
}

export interface SaveRestrictionCriterionRequest {
  ingredientId?: number;
  ingredientPattern?: string;
  nutrientId?: number;
  maxAmountPerServing?: number;
  severity: number;
  notes?: string;
}
