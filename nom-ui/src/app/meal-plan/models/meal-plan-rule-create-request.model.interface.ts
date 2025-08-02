// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-create-request.interface.ts

export interface IMealPlanRuleCreateRequestModel {
    householdId: number;
    dayOfWeekId: number;
    mealTypeId: number;
    queryFilterString?: string;
} 