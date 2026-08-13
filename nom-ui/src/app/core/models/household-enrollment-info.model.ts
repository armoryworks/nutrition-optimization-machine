/** External-management enrollment info for a household (nom-api bridge). */
export interface HouseholdEnrollmentInfo {
  /** Opaque external-management marker (e.g. "brigade:123"), or null when self-managed. */
  managedBy: string | null;
  /** Human-readable provider name; null until Brigade exposes a directory lookup. */
  providerDisplayName: string | null;
}
