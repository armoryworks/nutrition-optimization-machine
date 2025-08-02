// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-create-response.class.ts

import { IMealPlanRuleCreateResponseModel } from './meal-plan-rule-create-response.model.interface';

export class MealPlanRuleCreateResponseModel implements IMealPlanRuleCreateResponseModel {
    id: number = 0;
    householdId: number = 0;
    dayOfWeekId: number = 0;
    mealTypeId: number = 0;
    queryFilterString?: string;
    createdDate: Date = new Date();

    constructor(data?: Partial<IMealPlanRuleCreateResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 