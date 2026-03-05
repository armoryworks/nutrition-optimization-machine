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

export interface RecipeSearchResponse {
  results: RecipeSearchResult[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface RecipeSearchResult {
  id: number;
  name: string;
  description?: string;
  imageUrl?: string;
  prepTimeMinutes: number;
  cookTimeMinutes: number;
  totalTimeMinutes: number;
  prepTime: number;
  cookTime: number;
  totalTime: number;
  servings: number;
  rating?: number;
  ratingCount: number;
  authorName: string;
  averageRating: number;
  isPublic: boolean;
  isApproved: boolean;
  categories: string[];
  tags: string[];
  cuisineTypes: string[];
  ingredients?: RecipeIngredientSearchResult[];
  steps?: RecipeStepSearchResult[];
}

export interface RecipeIngredientSearchResult {
  id: number;
  name: string;
  quantity?: number;
  measurement?: string;
  notes?: string;
}

export interface RecipeStepSearchResult {
  id: number;
  stepNumber: number;
  instructions: string;
}
