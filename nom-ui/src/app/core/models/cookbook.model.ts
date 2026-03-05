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

export interface CookbookCreateRequest {
  householdId: number;
  name: string;
  description?: string;
  isPublic: boolean;
}

export interface CookbookUpdateRequest {
  name?: string;
  description?: string;
  isPublic?: boolean;
}
