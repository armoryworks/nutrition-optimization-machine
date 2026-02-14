export interface SmartShoppingListItemModel {
    id: number;
    name: string;
    quantity: number;
    unit: string;
    category: string;
    notes?: string;
    estimatedPrice?: number;
    brand?: string;
    store?: string;
    isPantryItem: boolean;
    isSubstitution: boolean;
    originalItem?: string;
    recipeSources: string[];
    nutritionalInfo: string[];
    priority: number;
}

export interface SmartShoppingListRequestModel {
    householdId: number;
    recipeIds: number[];
    planIds: number[];
    preferences: string[];
    dietaryRestrictions: string[];
    servingSize?: number;
    includePantryItems: boolean;
    optimizeForBudget: boolean;
    optimizeForNutrition: boolean;
    storePreference?: string;
}

export interface SmartShoppingListResponseModel {
    shoppingListId: number;
    shoppingListName: string;
    items: SmartShoppingListItemModel[];
    categories: string[];
    estimatedTotal: number;
    totalItems: number;
    generationMethod: string;
    recommendations: string[];
    substitutions: string[];
    warnings: string[];
}

export interface AIShoppingListRequestModel {
    description: string;
    ingredients: string[];
    meals: string[];
    preferences: string[];
    dietaryRestrictions: string[];
    servingSize?: number;
    daysToPlan?: number;
    budgetLimit?: number;
    storePreference?: string;
    includePantryItems: boolean;
    optimizeForBudget: boolean;
    optimizeForNutrition: boolean;
}

export interface AIShoppingListResponseModel {
    success: boolean;
    message: string;
    shoppingList?: SmartShoppingListResponseModel;
    suggestions: ShoppingListSuggestionModel[];
    errors: string[];
    aiReasoning?: string;
}

export interface ShoppingListOptimizationModel {
    shoppingListId: number;
    optimizeForBudget: boolean;
    optimizeForNutrition: boolean;
    optimizeForTime: boolean;
    budgetLimit?: number;
    storePreferences: string[];
    dietaryRestrictions: string[];
    excludedItems: string[];
}

export interface ShoppingListSuggestionModel {
    type: string;
    description: string;
    costSavings?: number;
    nutritionalBenefit?: string;
    timeBenefit?: string;
    items: string[];
    confidence: number;
}

export interface ShoppingListAnalyticsModel {
    shoppingListId: number;
    totalCost: number;
    averageItemCost: number;
    totalItems: number;
    completedItems: number;
    completionRate: number;
    categories: string[];
    categoryBreakdown: Record<string, number>;
    mostExpensiveItems: string[];
    mostPurchasedItems: string[];
    budgetUtilization: number;
    nutritionalScore?: string;
    recommendations: string[];
}

export interface ShoppingListTemplateModel {
    id: number;
    name: string;
    description: string;
    defaultItems: SmartShoppingListItemModel[];
    categories: string[];
    tags: string[];
    isPublic: boolean;
    usageCount: number;
}

export interface ShoppingListGenerationHistoryModel {
    id: number;
    shoppingListId: number;
    generationMethod: string;
    requestData: string;
    responseData: string;
    success: boolean;
    errorMessage?: string;
    generatedDate: string;
    generatedByUserId: number;
    processingTime: number;
    recipeCount: number;
    itemCount: number;
    estimatedCost: number;
    optimizationApplied: boolean;
}
