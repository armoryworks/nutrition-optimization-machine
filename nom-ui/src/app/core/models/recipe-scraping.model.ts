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

export interface ScrapeUrlRequest {
  url: string;
  useOpenAI?: boolean;
}

export interface ImportFromUrlRequest {
  url: string;
  importKeywordsAsTags?: boolean;
  stayInEditMode?: boolean;
}

export interface RecipeScrapingResponseModel {
  recipeId: number;
  recipeName: string;
  message: string;
  success: boolean;
  error: string | null;
}
