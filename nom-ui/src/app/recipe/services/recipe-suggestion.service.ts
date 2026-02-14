import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

// Interfaces matching backend models
export interface RecipeSuggestionQuery {
    limit?: number;
    maxMissingIngredients?: number;
    maxMissingTools?: number;
    includeIngredientsOnHand?: boolean;
    includeToolsOnHand?: boolean;
    queryFilter?: string;
    categories?: string[];
    tags?: string[];
    dietaryRestrictions?: string[];
    maxPrepTime?: number;
    maxCookTime?: number;
    maxDifficulty?: number;
    cuisines?: string[];
    includePublicRecipes?: boolean;
    includePrivateRecipes?: boolean;
}

export interface AIRecipeSuggestionRequest {
    description: string;
    availableIngredients?: string[];
    availableTools?: string[];
    preferences?: string[];
    dietaryRestrictions?: string[];
    dislikedIngredients?: string[];
    servingSize?: number;
    maxPrepTime?: number;
    maxCookTime?: number;
    budgetLimit?: number;
    cuisine?: string;
    mealType?: string;
    difficulty?: string;
    includeNutritionalInfo?: boolean;
    includeSubstitutions?: boolean;
}

export interface RecipeSuggestionResponseItem {
    recipeId: number;
    recipeName: string;
    description?: string;
    imageUrl?: string;
    rating: number;
    ratingCount: number;
    prepTime?: string;
    cookTime?: string;
    totalTime?: string;
    servings: number;
    difficulty: string;
    cuisine: string;
    categories: string[];
    tags: string[];
    missingIngredients: string[];
    missingTools: string[];
    matchScore: number;
    matchReason: string;
    substitutions: string[];
    nutritionalInfo?: Record<string, number>;
    estimatedCost?: number;
    isPublic: boolean;
    authorName: string;
    createdDate: string;
}

export interface RecipeSuggestionResponse {
    items: RecipeSuggestionResponseItem[];
    totalCount: number;
    suggestionMethod: string;
    recommendations: string[];
    analytics?: Record<string, number | string>;
}

export interface AIRecipeSuggestionResponse {
    success: boolean;
    message: string;
    suggestions: RecipeSuggestionResponseItem[];
    recommendations: string[];
    substitutions: string[];
    errors: string[];
    aiReasoning?: string;
    nutritionalAnalysis?: Record<string, number>;
    estimatedTotalCost?: number;
}

export interface RecipeRecommendation {
    recipeId: number;
    recipeName: string;
    recommendationType: string;
    confidence: number;
    reason: string;
    similarRecipes: string[];
    userBehaviorData?: Record<string, number | string | boolean>;
}

export interface RecipeDiscoveryRequest {
    ingredients?: string[];
    excludedIngredients?: string[];
    cuisines?: string[];
    dietaryRestrictions?: string[];
    mealTypes?: string[];
    maxPrepTime?: number;
    maxCookTime?: number;
    difficulty?: string;
    maxCost?: number;
    includeSeasonalRecipes?: boolean;
    includeTrendingRecipes?: boolean;
    includePersonalizedRecipes?: boolean;
    limit?: number;
}

export interface RecipeSimilarity {
    recipeId: number;
    recipeName: string;
    similarityScore: number;
    commonIngredients: string[];
    commonCategories: string[];
    commonTags: string[];
    similarityReason: string;
}

export interface RecipeTrending {
    recipeId: number;
    recipeName: string;
    trendingReason: string;
    viewCount: number;
    ratingCount: number;
    commentCount: number;
    averageRating: number;
    trendingStartDate: string;
    trendingFactors: string[];
}

export interface SeasonalRecipe {
    recipeId: number;
    recipeName: string;
    season: string;
    seasonalIngredients: string[];
    seasonalReason: string;
    seasonalScore: number;
}

export interface RecipeSuggestionAnalytics {
    totalSuggestions: number;
    matchedRecipes: number;
    partialMatches: number;
    averageMatchScore: number;
    topCategories: string[];
    topCuisines: string[];
    mostRequestedIngredients: string[];
    difficultyDistribution: Record<string, number>;
    costDistribution: Record<string, number>;
    popularSubstitutions: string[];
}

@Injectable({
    providedIn: 'root'
})
export class RecipeSuggestionService {
    private http = inject(HttpClient);

    private readonly baseUrl = `${environment.apiUrl}/RecipeSuggestion`;



    /**
     * Get recipe suggestions based on available ingredients and tools
     */
    getRecipeSuggestions(
        query: RecipeSuggestionQuery,
        ingredientIds?: number[],
        toolIds?: number[]
    ): Observable<RecipeSuggestionResponse> {
        let params = new HttpParams();

        // Add query parameters
        if (query.limit) params = params.set('limit', query.limit.toString());
        if (query.maxMissingIngredients) params = params.set('maxMissingIngredients', query.maxMissingIngredients.toString());
        if (query.maxMissingTools) params = params.set('maxMissingTools', query.maxMissingTools.toString());
        if (query.includeIngredientsOnHand !== undefined) params = params.set('includeIngredientsOnHand', query.includeIngredientsOnHand.toString());
        if (query.includeToolsOnHand !== undefined) params = params.set('includeToolsOnHand', query.includeToolsOnHand.toString());
        if (query.queryFilter) params = params.set('queryFilter', query.queryFilter);
        if (query.maxPrepTime) params = params.set('maxPrepTime', query.maxPrepTime.toString());
        if (query.maxCookTime) params = params.set('maxCookTime', query.maxCookTime.toString());
        if (query.maxDifficulty) params = params.set('maxDifficulty', query.maxDifficulty.toString());
        if (query.includePublicRecipes !== undefined) params = params.set('includePublicRecipes', query.includePublicRecipes.toString());
        if (query.includePrivateRecipes !== undefined) params = params.set('includePrivateRecipes', query.includePrivateRecipes.toString());

        // Add ingredient and tool IDs
        if (ingredientIds?.length) {
            ingredientIds.forEach(id => params = params.append('ingredientIds', id.toString()));
        }
        if (toolIds?.length) {
            toolIds.forEach(id => params = params.append('toolIds', id.toString()));
        }

        return this.http.get<RecipeSuggestionResponse>(`${this.baseUrl}/suggestions`, { params });
    }

    /**
     * Generate keyword-based recipe suggestions (not AI/ML-powered)
     */
    generateAISuggestions(request: AIRecipeSuggestionRequest): Observable<AIRecipeSuggestionResponse> {
        return this.http.post<AIRecipeSuggestionResponse>(`${this.baseUrl}/keyword-suggestions`, request);
    }

    /**
     * Get recipe recommendations based on user behavior
     */
    getRecipeRecommendations(): Observable<RecipeRecommendation[]> {
        return this.http.get<RecipeRecommendation[]>(`${this.baseUrl}/recommendations`);
    }

    /**
     * Discover recipes based on various criteria
     */
    discoverRecipes(request: RecipeDiscoveryRequest): Observable<RecipeSuggestionResponse> {
        return this.http.post<RecipeSuggestionResponse>(`${this.baseUrl}/discover`, request);
    }

    /**
     * Get similar recipes to a given recipe
     */
    getSimilarRecipes(recipeId: number, limit = 10): Observable<RecipeSimilarity[]> {
        const params = new HttpParams().set('limit', limit.toString());
        return this.http.get<RecipeSimilarity[]>(`${this.baseUrl}/similar/${recipeId}`, { params });
    }

    /**
     * Get trending recipes
     */
    getTrendingRecipes(limit = 10): Observable<RecipeTrending[]> {
        const params = new HttpParams().set('limit', limit.toString());
        return this.http.get<RecipeTrending[]>(`${this.baseUrl}/trending`, { params });
    }

    /**
     * Get seasonal recipe suggestions
     */
    getSeasonalRecipes(season?: string): Observable<SeasonalRecipe[]> {
        let params = new HttpParams();
        if (season) {
            params = params.set('season', season);
        }
        return this.http.get<SeasonalRecipe[]>(`${this.baseUrl}/seasonal`, { params });
    }

    /**
     * Get recipe suggestions for a specific meal type
     */
    getMealTypeSuggestions(mealType: string, query: RecipeSuggestionQuery): Observable<RecipeSuggestionResponse> {
        let params = new HttpParams();

        // Add query parameters
        if (query.limit) params = params.set('limit', query.limit.toString());
        if (query.maxMissingIngredients) params = params.set('maxMissingIngredients', query.maxMissingIngredients.toString());
        if (query.maxMissingTools) params = params.set('maxMissingTools', query.maxMissingTools.toString());
        if (query.includeIngredientsOnHand !== undefined) params = params.set('includeIngredientsOnHand', query.includeIngredientsOnHand.toString());
        if (query.includeToolsOnHand !== undefined) params = params.set('includeToolsOnHand', query.includeToolsOnHand.toString());
        if (query.queryFilter) params = params.set('queryFilter', query.queryFilter);
        if (query.maxPrepTime) params = params.set('maxPrepTime', query.maxPrepTime.toString());
        if (query.maxCookTime) params = params.set('maxCookTime', query.maxCookTime.toString());
        if (query.maxDifficulty) params = params.set('maxDifficulty', query.maxDifficulty.toString());
        if (query.includePublicRecipes !== undefined) params = params.set('includePublicRecipes', query.includePublicRecipes.toString());
        if (query.includePrivateRecipes !== undefined) params = params.set('includePrivateRecipes', query.includePrivateRecipes.toString());

        return this.http.get<RecipeSuggestionResponse>(`${this.baseUrl}/meal-type/${mealType}`, { params });
    }

    /**
     * Get recipe suggestions based on dietary restrictions
     */
    getDietarySuggestions(dietaryRestrictions: string[], query: RecipeSuggestionQuery): Observable<RecipeSuggestionResponse> {
        let params = new HttpParams();

        // Add query parameters
        if (query.limit) params = params.set('limit', query.limit.toString());
        if (query.maxMissingIngredients) params = params.set('maxMissingIngredients', query.maxMissingIngredients.toString());
        if (query.maxMissingTools) params = params.set('maxMissingTools', query.maxMissingTools.toString());
        if (query.includeIngredientsOnHand !== undefined) params = params.set('includeIngredientsOnHand', query.includeIngredientsOnHand.toString());
        if (query.includeToolsOnHand !== undefined) params = params.set('includeToolsOnHand', query.includeToolsOnHand.toString());
        if (query.queryFilter) params = params.set('queryFilter', query.queryFilter);
        if (query.maxPrepTime) params = params.set('maxPrepTime', query.maxPrepTime.toString());
        if (query.maxCookTime) params = params.set('maxCookTime', query.maxCookTime.toString());
        if (query.maxDifficulty) params = params.set('maxDifficulty', query.maxDifficulty.toString());
        if (query.includePublicRecipes !== undefined) params = params.set('includePublicRecipes', query.includePublicRecipes.toString());
        if (query.includePrivateRecipes !== undefined) params = params.set('includePrivateRecipes', query.includePrivateRecipes.toString());

        return this.http.post<RecipeSuggestionResponse>(`${this.baseUrl}/dietary`, dietaryRestrictions, { params });
    }

    /**
     * Get recipe suggestions based on cuisine preferences
     */
    getCuisineSuggestions(cuisines: string[], query: RecipeSuggestionQuery): Observable<RecipeSuggestionResponse> {
        let params = new HttpParams();

        // Add query parameters
        if (query.limit) params = params.set('limit', query.limit.toString());
        if (query.maxMissingIngredients) params = params.set('maxMissingIngredients', query.maxMissingIngredients.toString());
        if (query.maxMissingTools) params = params.set('maxMissingTools', query.maxMissingTools.toString());
        if (query.includeIngredientsOnHand !== undefined) params = params.set('includeIngredientsOnHand', query.includeIngredientsOnHand.toString());
        if (query.includeToolsOnHand !== undefined) params = params.set('includeToolsOnHand', query.includeToolsOnHand.toString());
        if (query.queryFilter) params = params.set('queryFilter', query.queryFilter);
        if (query.maxPrepTime) params = params.set('maxPrepTime', query.maxPrepTime.toString());
        if (query.maxCookTime) params = params.set('maxCookTime', query.maxCookTime.toString());
        if (query.maxDifficulty) params = params.set('maxDifficulty', query.maxDifficulty.toString());
        if (query.includePublicRecipes !== undefined) params = params.set('includePublicRecipes', query.includePublicRecipes.toString());
        if (query.includePrivateRecipes !== undefined) params = params.set('includePrivateRecipes', query.includePrivateRecipes.toString());

        return this.http.post<RecipeSuggestionResponse>(`${this.baseUrl}/cuisine`, cuisines, { params });
    }

    /**
     * Get quick recipe suggestions based on available time
     */
    getQuickRecipeSuggestions(maxTimeMinutes: number, query: RecipeSuggestionQuery): Observable<RecipeSuggestionResponse> {
        let params = new HttpParams().set('maxTimeMinutes', maxTimeMinutes.toString());

        // Add query parameters
        if (query.limit) params = params.set('limit', query.limit.toString());
        if (query.maxMissingIngredients) params = params.set('maxMissingIngredients', query.maxMissingIngredients.toString());
        if (query.maxMissingTools) params = params.set('maxMissingTools', query.maxMissingTools.toString());
        if (query.includeIngredientsOnHand !== undefined) params = params.set('includeIngredientsOnHand', query.includeIngredientsOnHand.toString());
        if (query.includeToolsOnHand !== undefined) params = params.set('includeToolsOnHand', query.includeToolsOnHand.toString());
        if (query.queryFilter) params = params.set('queryFilter', query.queryFilter);
        if (query.maxPrepTime) params = params.set('maxPrepTime', query.maxPrepTime.toString());
        if (query.maxCookTime) params = params.set('maxCookTime', query.maxCookTime.toString());
        if (query.maxDifficulty) params = params.set('maxDifficulty', query.maxDifficulty.toString());
        if (query.includePublicRecipes !== undefined) params = params.set('includePublicRecipes', query.includePublicRecipes.toString());
        if (query.includePrivateRecipes !== undefined) params = params.set('includePrivateRecipes', query.includePrivateRecipes.toString());

        return this.http.get<RecipeSuggestionResponse>(`${this.baseUrl}/quick`, { params });
    }

    /**
     * Get budget recipe suggestions
     */
    getBudgetRecipeSuggestions(maxBudget: number, query: RecipeSuggestionQuery): Observable<RecipeSuggestionResponse> {
        let params = new HttpParams().set('maxBudget', maxBudget.toString());

        // Add query parameters
        if (query.limit) params = params.set('limit', query.limit.toString());
        if (query.maxMissingIngredients) params = params.set('maxMissingIngredients', query.maxMissingIngredients.toString());
        if (query.maxMissingTools) params = params.set('maxMissingTools', query.maxMissingTools.toString());
        if (query.includeIngredientsOnHand !== undefined) params = params.set('includeIngredientsOnHand', query.includeIngredientsOnHand.toString());
        if (query.includeToolsOnHand !== undefined) params = params.set('includeToolsOnHand', query.includeToolsOnHand.toString());
        if (query.queryFilter) params = params.set('queryFilter', query.queryFilter);
        if (query.maxPrepTime) params = params.set('maxPrepTime', query.maxPrepTime.toString());
        if (query.maxCookTime) params = params.set('maxCookTime', query.maxCookTime.toString());
        if (query.maxDifficulty) params = params.set('maxDifficulty', query.maxDifficulty.toString());
        if (query.includePublicRecipes !== undefined) params = params.set('includePublicRecipes', query.includePublicRecipes.toString());
        if (query.includePrivateRecipes !== undefined) params = params.set('includePrivateRecipes', query.includePrivateRecipes.toString());

        return this.http.get<RecipeSuggestionResponse>(`${this.baseUrl}/budget`, { params });
    }

    /**
     * Get beginner recipe suggestions
     */
    getBeginnerRecipeSuggestions(query: RecipeSuggestionQuery): Observable<RecipeSuggestionResponse> {
        let params = new HttpParams();

        // Add query parameters
        if (query.limit) params = params.set('limit', query.limit.toString());
        if (query.maxMissingIngredients) params = params.set('maxMissingIngredients', query.maxMissingIngredients.toString());
        if (query.maxMissingTools) params = params.set('maxMissingTools', query.maxMissingTools.toString());
        if (query.includeIngredientsOnHand !== undefined) params = params.set('includeIngredientsOnHand', query.includeIngredientsOnHand.toString());
        if (query.includeToolsOnHand !== undefined) params = params.set('includeToolsOnHand', query.includeToolsOnHand.toString());
        if (query.queryFilter) params = params.set('queryFilter', query.queryFilter);
        if (query.maxPrepTime) params = params.set('maxPrepTime', query.maxPrepTime.toString());
        if (query.maxCookTime) params = params.set('maxCookTime', query.maxCookTime.toString());
        if (query.maxDifficulty) params = params.set('maxDifficulty', query.maxDifficulty.toString());
        if (query.includePublicRecipes !== undefined) params = params.set('includePublicRecipes', query.includePublicRecipes.toString());
        if (query.includePrivateRecipes !== undefined) params = params.set('includePrivateRecipes', query.includePrivateRecipes.toString());

        return this.http.get<RecipeSuggestionResponse>(`${this.baseUrl}/beginner`, { params });
    }

    /**
     * Get advanced recipe suggestions
     */
    getAdvancedRecipeSuggestions(query: RecipeSuggestionQuery): Observable<RecipeSuggestionResponse> {
        let params = new HttpParams();

        // Add query parameters
        if (query.limit) params = params.set('limit', query.limit.toString());
        if (query.maxMissingIngredients) params = params.set('maxMissingIngredients', query.maxMissingIngredients.toString());
        if (query.maxMissingTools) params = params.set('maxMissingTools', query.maxMissingTools.toString());
        if (query.includeIngredientsOnHand !== undefined) params = params.set('includeIngredientsOnHand', query.includeIngredientsOnHand.toString());
        if (query.includeToolsOnHand !== undefined) params = params.set('includeToolsOnHand', query.includeToolsOnHand.toString());
        if (query.queryFilter) params = params.set('queryFilter', query.queryFilter);
        if (query.maxPrepTime) params = params.set('maxPrepTime', query.maxPrepTime.toString());
        if (query.maxCookTime) params = params.set('maxCookTime', query.maxCookTime.toString());
        if (query.maxDifficulty) params = params.set('maxDifficulty', query.maxDifficulty.toString());
        if (query.includePublicRecipes !== undefined) params = params.set('includePublicRecipes', query.includePublicRecipes.toString());
        if (query.includePrivateRecipes !== undefined) params = params.set('includePrivateRecipes', query.includePrivateRecipes.toString());

        return this.http.get<RecipeSuggestionResponse>(`${this.baseUrl}/advanced`, { params });
    }

    /**
     * Get recipe suggestion analytics
     */
    getSuggestionAnalytics(): Observable<RecipeSuggestionAnalytics> {
        return this.http.get<RecipeSuggestionAnalytics>(`${this.baseUrl}/analytics`);
    }

    /**
     * Update recipe suggestion preferences
     */
    updateSuggestionPreferences(preferences: Record<string, number | string | boolean>): Observable<boolean> {
        return this.http.put<boolean>(`${this.baseUrl}/preferences`, preferences);
    }

    /**
     * Get recipe suggestion preferences
     */
    getSuggestionPreferences(): Observable<Record<string, number | string | boolean>> {
        return this.http.get<Record<string, number | string | boolean>>(`${this.baseUrl}/preferences`);
    }
} 