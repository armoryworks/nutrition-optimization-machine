import { RecipeSearchResult } from './recipe-search-result.model';

export interface RecipeSearchResponse {
  results: RecipeSearchResult[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}
