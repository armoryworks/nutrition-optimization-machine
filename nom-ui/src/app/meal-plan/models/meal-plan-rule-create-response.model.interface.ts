// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-create-response.interface.ts

export interface IMealPlanRuleCreateResponseModel {
    id: number;
    householdId: number;
    dayOfWeekId: number;
    mealTypeId: number;
    queryFilterString?: string;
    createdDate: Date;
} 