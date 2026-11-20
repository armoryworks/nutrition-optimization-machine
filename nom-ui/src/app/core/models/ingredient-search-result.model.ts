export interface IngredientSearchResult {
  id: number;
  name: string;
  fdcId?: string;
  matchedAlias?: string;
  matchedAliasTrusted?: boolean;
  foodGroupName?: string;
  hasNutrition?: boolean;
  canWeighVolume?: boolean;
  canWeighCount?: boolean;
}
