import { HouseholdMemberResponseModel } from './household-member-response.model';

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
