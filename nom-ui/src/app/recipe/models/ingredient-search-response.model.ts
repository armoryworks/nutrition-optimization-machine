// File: nom-ui/src/app/recipe/models/ingredient-search-response.model.ts
export interface IngredientSearchResponseModel {
  id: number;
  name: string;
  fdcId?: string;
  matchedAlias?: string; // The alias that matched the search term, if any
}