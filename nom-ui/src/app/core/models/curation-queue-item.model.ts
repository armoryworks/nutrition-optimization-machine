export interface CurationQueueItem {
  id: number;
  entityId: number;
  entityType: string;
  entityName: string;
  authorName: string;
  submittedDate: string;
  status: string;
  feedbackNotes: string | null;
}
