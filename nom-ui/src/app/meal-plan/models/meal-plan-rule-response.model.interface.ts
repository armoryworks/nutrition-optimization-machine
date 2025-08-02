// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-response.interface.ts

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