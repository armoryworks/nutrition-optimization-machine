// File: nom-ui/src/app/meal-plan/models/meal-plan-model.interface.ts

export interface IMealPlanModel {
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
    authorId: number;
    createdById: number;
    userId: number;
} 