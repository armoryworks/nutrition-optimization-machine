export interface RecipeSummaryModel {
    id: number;
    name: string;
    rating: number;
    createdDate: Date;
    imageUrl?: string;
}

export interface IngredientUsageModel {
    ingredientId: number;
    name: string;
    usageCount: number;
}

export interface RecipeDashboardAnalyticsModel {
    totalRecipes: number;
    recipesByStatus: { [key: string]: number };
    topRatedRecipes: RecipeSummaryModel[];
    recentlyCreated: RecipeSummaryModel[];
    mostUsedIngredients: IngredientUsageModel[];
}
