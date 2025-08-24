// File: nom-ui/src/app/meal-plan/models/meal-plan-response.interface.ts

export interface IMealPlanResponseModel {
    id: number;
    householdId: number;
    date: Date;
    mealTypeId: number;
    mealType: string;
    title: string;
    notes?: string;
    description?: string;
    recipeId?: number;
    recipeName?: string;
    groupName?: string;
    createdDate: Date;
    modifiedDate?: Date;
    authorName?: string;
} 