// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-response.class.ts

import { IMealPlanRuleResponseModel } from './meal-plan-rule-response.model.interface';

export class MealPlanRuleResponseModel implements IMealPlanRuleResponseModel {
    id = 0;
    householdId = 0;
    dayOfWeekId = 0;
    dayOfWeek = '';
    mealTypeId = 0;
    mealType = '';
    queryFilterString?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IMealPlanRuleResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 