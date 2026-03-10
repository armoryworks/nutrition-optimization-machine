export interface RecipeSearchRequest {
  query?: string;
  ingredientIds?: number[];
  categoryIds?: number[];
  tagIds?: number[];
  toolIds?: number[];
  cuisineTypeIds?: number[];
  minRating?: number;
  maxPrepTime?: number;
  maxCookTime?: number;
  maxTotalTime?: number;
  isPublic?: boolean;
  isApproved?: boolean;
  sortBy?: string;
  sortDirection?: string;
  page: number;
  pageSize: number;
  includeIngredients: boolean;
  includeSteps: boolean;
  includeNutrition: boolean;
}
