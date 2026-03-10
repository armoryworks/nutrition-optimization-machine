export interface CreateThreadRequest {
  participantPersonIds: number[];
  subject: string;
  initialMessage: string;
  threadType?: number;
  recipeId?: number | null;
  ingredientId?: number | null;
  planId?: number | null;
}
