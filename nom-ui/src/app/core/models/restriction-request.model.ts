export interface RestrictionRequest {
  name: string;
  description: string | null;
  restrictionTypeId: number;
  appliesToEntirePlan: boolean;
  affectedPersonIds: number[] | null;
}
