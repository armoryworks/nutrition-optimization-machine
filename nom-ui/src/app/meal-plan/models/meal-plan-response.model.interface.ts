// File: nom-ui/src/app/meal-plan/models/meal-plan-response.interface.ts

export interface IMealPlanResponseModel {
    id: number;
    householdId: number;
    date: Date;
    mealTypeId: number;
    title?: string;
    notes?: string;
    recipeId?: number;
    createdDate: Date;
    modifiedDate?: Date;
} 