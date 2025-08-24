// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-response.class.ts

import { IMealPlanRuleResponseModel } from './meal-plan-rule-response.model.interface';

export class MealPlanRuleResponseModel implements IMealPlanRuleResponseModel {
    id = 0;
    name = '';
    description?: string;
    dayOfWeekId = 0;
    dayOfWeek?: string;
    dayOfWeekName?: string;
    mealTypeId = 0;
    mealType?: string;
    mealTypeName?: string;
    queryFilterString?: string;
    householdId = 0;
    isActive = true;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IMealPlanRuleResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 