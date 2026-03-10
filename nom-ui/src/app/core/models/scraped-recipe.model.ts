export interface ScrapedRecipeModel {
  name: string;
  description: string | null;
  image: string | null;
  sourceUrl: string | null;
  sourceSite: string | null;
  prepTime: string | null;
  cookTime: string | null;
  totalTime: string | null;
  recipeYield: string | null;
  ingredients: string[];
  steps: string[];
  tags: string[];
  categories: string[];
}
