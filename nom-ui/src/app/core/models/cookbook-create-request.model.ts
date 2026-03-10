export interface CookbookCreateRequest {
  householdId: number;
  name: string;
  description?: string;
  isPublic: boolean;
}
