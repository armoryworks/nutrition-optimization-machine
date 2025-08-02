// File: nom-ui/src/app/meal-plan/models/meal-plan-response.interface.ts

export interface IMealPlanResponseModel {
    id: number;
    householdId: number;
    authorId: number;
    date: Date;
    mealTypeId: number;
    mealType: string;
    title: string;
    notes?: string;
    recipeId?: number;
    recipeName?: string;
    createdDate: Date;
    modifiedDate?: Date;
} 