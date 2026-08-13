export interface HouseholdMemberResponseModel {
  id: number;
  householdId: number;
  personId: number;
  personName: string;
  personEmail: string | null;
  role: string;
  joinedDate: string;
  isActive: boolean;
  hasProfile: boolean;
  hasRestrictions: boolean;
  /** True when this member may perform steward actions (policies, restriction locks). */
  isSteward?: boolean;
}
