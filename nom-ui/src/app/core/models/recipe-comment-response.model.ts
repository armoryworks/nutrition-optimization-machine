export interface RecipeCommentResponseModel {
  id: number;
  recipeId: number;
  authorId: number;
  authorName: string;
  comment: string;
  createdDate: string;
  lastModifiedDate: string | null;
}
