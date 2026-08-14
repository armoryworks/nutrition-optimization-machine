export interface IngredientLookupResult {
  id: number;
  name: string;
  fdcId: string | null;
  matchedAlias: string | null;
  foodGroupId: number | null;
  foodGroupName: string | null;
  isWholeFood: boolean | null;
}
