export interface RecipeSearchModel {
    query: string;
    categoryIds: number[];
    ingredientIds: number[];
    difficultyLevel?: number;
    maxPrepTime?: number;
    maxCookTime?: number;
    maxTotalTime?: number;
    minServings?: number;
    maxServings?: number;
    dietaryRestrictions: number[];
    allergens: number[];
    nutritionFilters: NutritionFilter[];
    sortBy: string;
    sortOrder: string;
    page: number;
    pageSize: number;
    includeCurated: boolean;
    includeUserCreated: boolean;
    maxResults: number;
}

export interface RecipeSearchResponse {
    recipes: RecipeSearchResult[];
    results: RecipeSearchResult[]; // Alias for recipes
    totalCount: number;
    page: number;
    pageSize: number;
    totalPages: number;
    hasNextPage: boolean;
    hasPreviousPage: boolean;
}

export interface RecipeSearchResult {
    id: number;
    name: string;
    description?: string;
    imageUrl?: string;
    prepTime: number;
    cookTime: number;
    totalTime: number;
    servings: number;
    averageRating: number;
    ratingCount: number;
    rating: number; // Alias for averageRating
    isPublic: boolean;
    isApproved: boolean;
    createdDate: Date;
    lastModifiedDate?: Date;
    authorId: number;
    authorName: string;
    categories: string[];
    tags: string[];
    cuisineTypes: string[];
    ingredients?: RecipeIngredientSearch[];
    steps?: RecipeStepSearch[];
    nutrition?: RecipeNutritionSearch;
}

export interface RecipeIngredientSearch {
    id: number;
    name: string;
    quantity?: number;
    measurement?: string;
    notes?: string;
}

export interface RecipeStepSearch {
    id: number;
    stepNumber: number;
    instructions: string;
    imageUrl?: string;
}

export interface RecipeNutritionSearch {
    calories?: number;
    protein?: number;
    carbohydrates?: number;
    fat?: number;
    fiber?: number;
    sugar?: number;
    sodium?: number;
} 