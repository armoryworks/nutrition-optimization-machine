import { RecipeIngredientModel } from './recipe-ingredient.model';
import { RecipeStepModel } from './recipe-step.model';
import { RecipeNutritionModel } from './recipe-nutrition.model';

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
