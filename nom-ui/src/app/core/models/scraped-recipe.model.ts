export interface ScrapedIngredientModel {
  name: string;
  quantity: number | null;
  unit: string | null;
  notes: string | null;
}

export interface ScrapedStepModel {
  order: number | null;
  instruction: string;
  image: string | null;
}

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
  ingredients: ScrapedIngredientModel[];
  steps: ScrapedStepModel[];
  tags: string[];
  categories: string[];
}
