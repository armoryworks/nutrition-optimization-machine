export type CurationQueueStatus = 'PendingCuration' | 'RequiresRevision';

/** Mirrors nom-api CurationQueueItemModel — field names must match its JSON exactly. */
export interface CurationQueueItem {
  /** The entity's own id (Recipe/Ingredient/Plan id) — used for decision requests. */
  id: number;
  entityType: 'Recipe' | 'Ingredient' | 'Plan' | string;
  name: string;
  /** Null for system-imported content with no author. */
  authorName: string | null;
  dateSubmitted: string;
  description?: string | null;
  sourceUrl?: string | null;
  authorId: number;
  /** Recipes flagged by import vetting arrive as RequiresRevision; ingredients/plans are always PendingCuration. */
  status: CurationQueueStatus;
  /** Newline-separated plausibility problems from import vetting; null when clean. */
  vettingIssues?: string | null;
  /** True while the description/steps still contain the source's verbatim text. */
  containsSourceProse: boolean;
  /** The source's hero image — for the reviewer's side-by-side comparison ONLY, never the recipe's image. */
  sourceImageUrl?: string | null;
  /** Recipes only: ingredients still blocking approval (approval is refused while non-empty). */
  uncuratedIngredients?: CurationBlockingIngredient[];
}

export interface CurationBlockingIngredient {
  id: number;
  name: string;
  statusId: number;
  /** "NonCurated" | "PendingCuration" | "RequiresRevision" | "Rejected" */
  status: string;
}
