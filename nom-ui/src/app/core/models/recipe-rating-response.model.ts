export interface RecipeRatingResponseModel {
  id: number;
  recipeId: number;
  raterId: number;
  raterName: string;
  rating: number;
  createdDate: string;
  lastModifiedDate: string | null;
}
