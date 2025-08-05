// File: nom-ui/src/app/meal-plan/models/meal-plan-rule.class.ts

import { IMealPlanRuleModel } from './meal-plan-rule.model.interface';
import { IMealPlanRuleCreateRequestModel } from './meal-plan-rule-create-request.model.interface';
import { IMealPlanRuleCreateResponseModel } from './meal-plan-rule-create-response.model.interface';
import { IMealPlanRuleResponseModel } from './meal-plan-rule-response.model.interface';

export class MealPlanRuleModel implements IMealPlanRuleModel {
    id: number = 0;
    householdId: number = 0;
    authorId: number = 0;
    name: string = '';
    description?: string;
    dayOfWeekId: number = 0;
    mealTypeId: number = 0;
    queryFilterString?: string;
    isActive: boolean = true;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IMealPlanRuleModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class MealPlanRuleCreateRequestModel implements IMealPlanRuleCreateRequestModel {
    householdId: number = 0;
    authorId: number = 0;
    name: string = '';
    description?: string;
    dayOfWeekId: number = 0;
    mealTypeId: number = 0;
    queryFilterString?: string;
    isActive: boolean = true;

    constructor(data?: Partial<IMealPlanRuleCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class MealPlanRuleCreateResponseModel implements IMealPlanRuleCreateResponseModel {
    id: number = 0;
    householdId: number = 0;
    authorId: number = 0;
    name: string = '';
    description?: string;
    dayOfWeekId: number = 0;
    mealTypeId: number = 0;
    queryFilterString?: string;
    isActive: boolean = true;
    createdDate: Date = new Date();

    constructor(data?: Partial<IMealPlanRuleCreateResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class MealPlanRuleResponseModel implements IMealPlanRuleResponseModel {
    id: number = 0;
    householdId: number = 0;
    authorId: number = 0;
    name: string = '';
    description?: string;
    dayOfWeekId: number = 0;
    dayOfWeek?: string;
    dayOfWeekName?: string;
    mealTypeId: number = 0;
    mealType?: string;
    mealTypeName?: string;
    queryFilterString?: string;
    isActive: boolean = true;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IMealPlanRuleResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 