import { RecipeIngredientSearchResult } from './recipe-ingredient-search-result.model';
import { RecipeStepSearchResult } from './recipe-step-search-result.model';
import { RecipeDietMatchModel } from './recipe.model';
import { RecipeImageCredit } from './recipe-image.model';

export interface RecipeSearchResult {
  id: number;
  name: string;
  description?: string;
  imageUrl?: string;
  /** Credit for a third-party openly licensed photo; absent for the author's own upload. */
  imageCredit?: RecipeImageCredit | null;
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
  dietConflicts?: RecipeDietMatchModel[];
}
