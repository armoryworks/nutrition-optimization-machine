// File: nom-ui/src/app/meal-plan/models/meal-plan-update-request.interface.ts

export interface IMealPlanUpdateRequestModel {
    date: Date;
    mealTypeId: number;
    title: string;
    notes?: string;
    recipeId?: number;
    recipeName?: string;
    description?: string;
} 