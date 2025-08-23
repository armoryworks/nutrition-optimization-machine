// File: nom-ui/src/app/meal-plan/models/meal-plan-create-response.interface.ts

export interface IMealPlanCreateResponseModel {
    id: number;
    householdId: number;
    date: Date;
    mealTypeId: number;
    title?: string;
    notes?: string;
    recipeId?: number;
    createdDate: Date;
} 