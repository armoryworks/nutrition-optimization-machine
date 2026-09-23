export interface IngredientSearchResult {
  id: number;
  name: string;
  fdcId?: string;
  matchedAlias?: string;
  foodGroupName?: string;
  hasNutrition?: boolean;
}
