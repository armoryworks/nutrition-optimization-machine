export type CurationQueueStatus = 'PendingCuration' | 'RequiresRevision';

export interface CurationQueueItem {
  id: number;
  entityId: number;
  entityType: string;
  entityName: string;
  authorName: string;
  submittedDate: string;
  /** Recipes flagged by import vetting arrive as RequiresRevision; ingredients/plans are always PendingCuration. */
  status: CurationQueueStatus;
  feedbackNotes: string | null;
  /** Newline-separated plausibility problems from import vetting; null when clean. */
  vettingIssues?: string | null;
  /** True while the description/steps still contain the source's verbatim text. */
  containsSourceProse: boolean;
  /** The source's hero image — for the reviewer's side-by-side comparison ONLY, never the recipe's image. */
  sourceImageUrl?: string | null;
}
