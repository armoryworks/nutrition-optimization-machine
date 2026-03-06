export interface RecipeModel {
  id: number;
  name: string;
  description: string;
  authorName: string;
  authorId: number;
  imageUrl?: string;
  prepTimeMinutes?: number;
  cookTimeMinutes?: number;
  servings?: number;
  rating: number;
  commentCount: number;
  ratingCount: number;
  createdDate: string;
  modifiedDate?: string;
  curationStatus: string;
  ingredients?: RecipeIngredientModel[];
  steps?: RecipeStepModel[];
  nutrition?: RecipeNutritionModel[];
}

export interface RecipeIngredientModel {
  ingredientId: number;
  name: string;
  quantity: number;
  measurementId: number;
  measurement?: string;
  notes?: string;
}

export interface RecipeStepModel {
  description: string;
  order: number;
}

export interface RecipeNutritionModel {
  nutrientName: string;
  amount: number;
  unit: string;
  dailyValuePercent?: number;
}

// ── Request models ──

export interface RecipeIngredientRequest {
  ingredientId: number;
  name: string;
  quantity: number;
  measurementId: number;
}

export interface RecipeStepRequest {
  description: string;
  order: number;
}

export interface RecipeCreateRequest {
  name: string;
  description: string;
  ingredients: RecipeIngredientRequest[];
  steps: RecipeStepRequest[];
}

export interface RecipeUpdateRequest {
  id: number;
  name: string;
  description?: string;
  ingredients: RecipeIngredientRequest[];
  steps: RecipeStepRequest[];
}

export interface RecipeCreateResponse {
  id: number;
  name: string;
  description: string;
  authorId: number;
  createdDate: string;
  message: string;
}

export interface RecipeAssetResponse {
  id: number;
  recipeId: number;
  name: string;
  fileExtension: string;
  contentType: string;
  fileSize: number;
  description?: string;
  createdDate: string;
}
