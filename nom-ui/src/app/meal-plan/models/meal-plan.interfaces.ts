// File: nom-ui/src/app/meal-plan/models/meal-plan.interfaces.ts

export interface IMealPlanModel {
    id: number;
    householdId: number;
    authorId: number;
    date: Date;
    mealTypeId: number;
    title: string;
    notes?: string;
    recipeId?: number;
    createdDate: Date;
    modifiedDate?: Date;
}

export interface IMealPlanCreateRequestModel {
    householdId: number;
    date: Date;
    mealTypeId: number;
    mealType?: string;
    title: string;
    notes?: string;
    recipeId?: number;
}

export interface IMealPlanCreateResponseModel {
    id: number;
    householdId: number;
    authorId: number;
    date: Date;
    mealTypeId: number;
    title: string;
    notes?: string;
    recipeId?: number;
    createdDate: Date;
}

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

export interface IMealPlanUpdateRequestModel {
    date: Date;
    mealTypeId: number;
    title: string;
    notes?: string;
    recipeId?: number;
}

export interface IMealPlanRuleModel {
    id: number;
    householdId: number;
    dayOfWeekId: number;
    mealTypeId: number;
    queryFilterString?: string;
    createdDate: Date;
    modifiedDate?: Date;
}

export interface IMealPlanRuleCreateRequestModel {
    householdId: number;
    dayOfWeekId: number;
    mealTypeId: number;
    queryFilterString?: string;
}

export interface IMealPlanRuleCreateResponseModel {
    id: number;
    householdId: number;
    dayOfWeekId: number;
    mealTypeId: number;
    queryFilterString?: string;
    createdDate: Date;
}

export interface IMealPlanRuleResponseModel {
    id: number;
    householdId: number;
    dayOfWeekId: number;
    dayOfWeek: string;
    mealTypeId: number;
    mealType: string;
    queryFilterString?: string;
    createdDate: Date;
    modifiedDate?: Date;
} 