// File: nom-ui/src/app/meal-plan/models/meal-plan-rule.class.ts

import { IMealPlanRuleModel } from './meal-plan-rule.model.interface';

export class MealPlanRuleModel implements IMealPlanRuleModel {
    id: number = 0;
    householdId: number = 0;
    dayOfWeekId: number = 0;
    mealTypeId: number = 0;
    queryFilterString?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IMealPlanRuleModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 