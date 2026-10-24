export interface CreateThreadRequest {
  participantIds: number[];
  initialMessage: string;
  threadType?: number;
  recipeId?: number | null;
  ingredientId?: number | null;
  planId?: number | null;
}
