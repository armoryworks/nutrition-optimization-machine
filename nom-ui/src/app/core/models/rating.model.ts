export interface RecipeRatingResponseModel {
  id: number;
  recipeId: number;
  raterId: number;
  raterName: string;
  rating: number;
  createdDate: string;
  lastModifiedDate: string | null;
}

export interface RecipeRatingCreateRequest {
  rating: number;
}

export interface RecipeRatingUpdateRequest {
  rating: number;
  comment?: string;
}
