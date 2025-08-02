// File: nom-ui/src/app/meal-plan/models/meal-plan-create-request.interface.ts

export interface IMealPlanCreateRequestModel {
    householdId: number;
    date: Date;
    mealTypeId: number;
    mealType?: string;
    title: string;
    notes?: string;
    recipeId?: number;
} 