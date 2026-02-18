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
