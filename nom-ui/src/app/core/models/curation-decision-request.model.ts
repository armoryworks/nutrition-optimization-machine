export interface CurationDecisionRequest {
  entityId: number;
  entityType: string;
  decisionNotes: string;
  /** Recipes: servings recorded on approval so the nutrition label can be per serving. */
  servings?: number | null;
}
