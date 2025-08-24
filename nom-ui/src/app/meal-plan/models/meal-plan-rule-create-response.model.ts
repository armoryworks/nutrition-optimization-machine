// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-create-response.class.ts

import { IMealPlanRuleCreateResponseModel } from './meal-plan-rule-create-response.model.interface';

export class MealPlanRuleCreateResponseModel implements IMealPlanRuleCreateResponseModel {
    id = 0;
    name = '';
    description?: string;
    dayOfWeekId = 0;
    mealTypeId = 0;
    queryFilterString?: string;
    householdId = 0;
    isActive = true;
    createdDate: Date = new Date();

    constructor(data?: Partial<IMealPlanRuleCreateResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 