// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-create-request.class.ts

import { IMealPlanRuleCreateRequestModel } from './meal-plan-rule-create-request.model.interface';

export class MealPlanRuleCreateRequestModel implements IMealPlanRuleCreateRequestModel {
    householdId: number = 0;
    dayOfWeekId: number = 0;
    mealTypeId: number = 0;
    queryFilterString?: string;

    constructor(data?: Partial<IMealPlanRuleCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 