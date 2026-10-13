export interface RecipeSearchFilterOption {
  id: number;
  name: string;
  group?: string | null;
}

export interface RecipeSearchFilterOptions {
  mealTypes: RecipeSearchFilterOption[];
  courses: RecipeSearchFilterOption[];
  kitchenTools: RecipeSearchFilterOption[];
}
