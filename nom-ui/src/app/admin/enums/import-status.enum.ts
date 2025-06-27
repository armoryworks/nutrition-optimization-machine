/**
 * Defines the possible statuses for an import job.
 * Must match the ImportStatusEnum in the backend (Nom.Data.Audit).
 */
export enum ImportStatusEnum {
  Queued = 0,
  Running = 1,
  Completed = 2,
  Failed = 3,
  Canceled = 4,
}
