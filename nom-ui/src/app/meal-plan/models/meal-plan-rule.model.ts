// File: nom-ui/src/app/meal-plan/models/meal-plan-rule.model.ts

import { IMealPlanRuleModel } from './meal-plan-rule.model.interface';

export class MealPlanRuleModel implements IMealPlanRuleModel {
    id = 0;
    name = '';
    description?: string;
    dayOfWeekId = 0;
    mealTypeId = 0;
    queryFilterString?: string;
    householdId = 0;
    isActive = true;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IMealPlanRuleModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

