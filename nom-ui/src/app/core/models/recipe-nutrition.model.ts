export interface RecipeNutritionModel {
  nutrientName: string;
  amount: number;
  unit: string;
  dailyValuePercent?: number | null;
  partial?: boolean;
}

export interface RecipeNutritionSource {
  kind: 'usda' | 'mixed' | 'catalog' | 'recipe';
  usdaLines: number;
  countedLines: number;
}

export interface RecipeAlcoholNote {
  drinks: string[];
  flavourings: string[];
  uncounted: string[];
  asWrittenGrams: number;
  retainedGrams: number;
  flavouringGrams: number;
  perServing: boolean;
  basis?: 'timed' | 'up-to' | 'flamed' | 'hot-liquid' | 'overnight' | 'no-heat' | 'no-steps' | null;
  method?: 'simmer' | 'bake' | null;
  minutes?: number | null;
  retainedPercent?: number | null;
  usdaCode?: string | null;
  notRead: boolean;
}
