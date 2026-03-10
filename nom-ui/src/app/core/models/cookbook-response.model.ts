export interface CookbookResponseModel {
  id: number;
  householdId: number;
  name: string;
  description: string | null;
  slug: string | null;
  isPublic: boolean;
  recipeCount: number;
  createdDate: string;
}
