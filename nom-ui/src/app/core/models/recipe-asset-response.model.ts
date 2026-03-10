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
