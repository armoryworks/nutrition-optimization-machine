export interface PlanRestrictionModel {
  id: number;
  name: string;
  description: string | null;
  restrictionType: string | null;
  ingredientName: string | null;
  nutrientName: string | null;
}
