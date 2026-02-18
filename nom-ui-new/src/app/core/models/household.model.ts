export interface HouseholdResponseModel {
  id: number;
  name: string;
  description: string | null;
  householdGroupId: number;
  createdDate: string;
  modifiedDate: string | null;
  members: HouseholdMemberResponseModel[] | null;
  memberCount: number;
  recipeCount: number;
  planCount: number;
  shoppingListCount: number;
}

export interface HouseholdCreateModel {
  name: string;
  description: string | null;
  householdGroupId: number;
}

export interface HouseholdCreateResponseModel {
  id: number;
  name: string;
  description: string | null;
  householdGroupId: number;
  createdDate: string;
}

export interface HouseholdUpdateModel {
  name: string;
  description: string | null;
  householdGroupId: number | null;
}

export interface HouseholdMemberResponseModel {
  id: number;
  householdId: number;
  personId: number;
  personName: string;
  personEmail: string | null;
  role: string;
  joinedDate: string;
  isActive: boolean;
}

export interface HouseholdMemberCreateModel {
  householdId: number;
  personId: number;
  role: string | null;
}

export interface HouseholdInviteTokenCreateModel {
  householdId: number;
  name: string | null;
  usesLeft: number | null;
  expirationDate: string | null;
}

export interface HouseholdInviteTokenResponseModel {
  id: number;
  householdId: number;
  token: string;
  createdDate: string;
}

export interface JoinHouseholdRequest {
  token: string;
}
