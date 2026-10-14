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
