export interface RecipeAdvancedSearchModel {
    query?: string;
    categoryIds?: number[];
    tagIds?: number[];
    toolIds?: number[];
    ingredientIds?: number[];
    cuisineTypeIds?: number[];
    householdIds?: number[];
    minRating?: number;
    maxPrepTime?: number;
    maxCookTime?: number;
    maxTotalTime?: number;
    isPublic?: boolean;
    isApproved?: boolean;
    sortBy?: string;
    sortDirection?: string;
    page: number;
    pageSize: number;
    includeIngredients: boolean;
    includeSteps: boolean;
    includeNutrition: boolean;
}

export interface RecipeSuggestionQueryModel {
    query?: string;
    foodIds?: number[];
    toolIds?: number[];
    limit: number;
    includeIngredients: boolean;
    includeSteps: boolean;
}

export interface RecipeSuggestionResultModel {
    id: number;
    name: string;
    description?: string;
    imageUrl?: string;
    rating?: number;
    ratingCount: number;
    categories: string[];
    tags: string[];
    ingredients?: RecipeSuggestionIngredient[];
    steps?: RecipeSuggestionStep[];
}

export interface RecipeSuggestionIngredient {
    id: number;
    name: string;
    quantity?: number;
    measurement?: string;
    notes?: string;
}

export interface RecipeSuggestionStep {
    id: number;
    stepNumber: number;
    instructions: string;
    imageUrl?: string;
}

export interface RecipeSuggestionResponseModel {
    suggestions: RecipeSuggestionResultModel[];
    totalCount: number;
}
