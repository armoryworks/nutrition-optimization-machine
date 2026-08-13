export interface RestrictionRequest {
  name: string;
  description: string | null;
  restrictionTypeId: number;
  appliesToEntirePlan: boolean;
  affectedPersonIds: number[] | null;
  /** Restriction id when returned as existing data; ignored on input. */
  id?: number | null;
  /** True when locked by a household steward or external manager (server-controlled). */
  locked?: boolean;
  /** Who locked it: "person:{id}" or an external manager marker (server-controlled). */
  lockedBy?: string | null;
}
