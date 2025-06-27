// nom-ui/src/app/admin/models/import-job-status-response.model.ts

import { ImportStatusEnum } from '../enums/import-status.enum';


/**
 * Represents the detailed status of an ongoing or completed import job.
 */
export interface ImportJobStatusResponseModel {
  /**
   * The unique identifier of the import process.
   */
  processId: string; // GUID as a string
  /**
   * A descriptive name for the job (e.g., "Kaggle Recipe Import").
   */
  jobName: string;
  /**
   * The current status of the job (e.g., Queued, Running, Completed, Failed).
   */
  status: ImportStatusEnum;
  /**
   * A human-readable message providing details about the current status or last action.
   */
  message: string;
  /**
   * The total number of records expected or found in the source.
   */
  totalRecords: number;
  /**
   * The number of records successfully imported.
   */
  importedCount: number;
  /**
   * The number of records skipped (e.g., duplicates, invalid data).
   */
  skippedCount: number;
  /**
   * The number of records that caused an error during processing.
   */
  errorCount: number;
  /**
   * The UTC timestamp when the job record was created.
   */
  createdAt: string; // ISO 8601 string
  /**
   * The UTC timestamp when the job started execution.
   */
  startedAt?: string; // Optional, ISO 8601 string
  /**
   * The UTC timestamp when the job completed or failed.
   */
  completedAt?: string; // Optional, ISO 8601 string
  /**
   * The ID of the person who initiated the import job.
   */
  createdByPersonId?: string; // Optional, can be null if initiated by system
}
