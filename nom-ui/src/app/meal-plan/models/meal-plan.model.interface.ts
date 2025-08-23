// File: nom-ui/src/app/meal-plan/models/meal-plan-model.interface.ts

export interface IMealPlanModel {
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