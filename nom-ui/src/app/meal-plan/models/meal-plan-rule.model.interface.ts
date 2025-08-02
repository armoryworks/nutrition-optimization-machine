// File: nom-ui/src/app/meal-plan/models/meal-plan-rule.interface.ts

export interface IMealPlanRuleModel {
    id: number;
    householdId: number;
    dayOfWeekId: number;
    mealTypeId: number;
    queryFilterString?: string;
    createdDate: Date;
    modifiedDate?: Date;
} 