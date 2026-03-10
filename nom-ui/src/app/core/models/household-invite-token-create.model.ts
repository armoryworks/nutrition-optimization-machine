export interface HouseholdInviteTokenCreateModel {
  householdId: number;
  name: string | null;
  usesLeft: number | null;
  expirationDate: string | null;
}
