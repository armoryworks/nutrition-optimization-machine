export interface HouseholdCreateResponseModel {
  id: number;
  name: string;
  description: string | null;
  householdGroupId: number;
  createdDate: string;
}
