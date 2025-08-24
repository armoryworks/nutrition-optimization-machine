// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-create-request.class.ts

import { IMealPlanRuleCreateRequestModel } from './meal-plan-rule-create-request.model.interface';

export class MealPlanRuleCreateRequestModel implements IMealPlanRuleCreateRequestModel {
    id = 0;
    name = '';
    description?: string;
    dayOfWeekId = 0;
    mealTypeId = 0;
    queryFilterString?: string;
    householdId = 0;
    isActive = true;

    constructor(data?: Partial<IMealPlanRuleCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 