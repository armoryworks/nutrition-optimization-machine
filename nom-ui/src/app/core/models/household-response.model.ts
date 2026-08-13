import { HouseholdMemberResponseModel } from './household-member-response.model';

export interface HouseholdResponseModel {
  id: number;
  name: string;
  description: string | null;
  householdGroupId: number;
  createdDate: string;
  modifiedDate: string | null;
  /** Opaque external-management marker (e.g. "brigade:123"), or null when self-managed. */
  managedBy?: string | null;
  /** True for a solo user's personal kitchen (no members/invites until converted). */
  isPersonal?: boolean;
  members: HouseholdMemberResponseModel[] | null;
  memberCount: number;
  recipeCount: number;
  planCount: number;
  shoppingListCount: number;
}
