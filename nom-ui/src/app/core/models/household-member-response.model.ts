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
}
