// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-response.class.ts

import { IMealPlanRuleResponseModel } from './meal-plan-rule-response.model.interface';

export class MealPlanRuleResponseModel implements IMealPlanRuleResponseModel {
    id: number = 0;
    householdId: number = 0;
    dayOfWeekId: number = 0;
    dayOfWeek: string = '';
    mealTypeId: number = 0;
    mealType: string = '';
    queryFilterString?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IMealPlanRuleResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 