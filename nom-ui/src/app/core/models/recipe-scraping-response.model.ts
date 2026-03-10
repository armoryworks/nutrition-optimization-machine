export interface RecipeScrapingResponseModel {
  recipeId: number;
  recipeName: string;
  message: string;
  success: boolean;
  error: string | null;
}
