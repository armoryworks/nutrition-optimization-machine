// File: nom-ui/src/app/meal-plan/models/meal-plan-model.class.ts

import { IMealPlanModel } from './meal-plan.model.interface';
import { IMealPlanResponseModel } from './meal-plan-response.model.interface';
import { IMealPlanCreateRequestModel } from './meal-plan-create-request.model.interface';
import { IMealPlanCreateResponseModel } from './meal-plan-create-response.model.interface';
import { IMealPlanUpdateRequestModel } from './meal-plan-update-request.model.interface';
import { IMealPlanRuleModel } from './meal-plan-rule.model.interface';
import { IMealPlanRuleCreateRequestModel } from './meal-plan-rule-create-request.model.interface';
import { IMealPlanRuleCreateResponseModel } from './meal-plan-rule-create-response.model.interface';
import { IMealPlanRuleResponseModel } from './meal-plan-rule-response.model.interface';

export class MealPlanModel implements IMealPlanModel {
    id: number = 0;
    householdId: number = 0;
    authorId: number = 0;
    date: Date = new Date();
    mealTypeId: number = 0;
    title: string = '';
    notes?: string;
    recipeId?: number;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IMealPlanModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class MealPlanResponseModel implements IMealPlanResponseModel {
    id: number = 0;
    householdId: number = 0;
    authorId: number = 0;
    date: Date = new Date();
    mealTypeId: number = 0;
    mealType: string = '';
    title: string = '';
    notes?: string;
    description?: string;
    recipeId?: number;
    recipeName?: string;
    authorName?: string;
    groupName?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IMealPlanResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class MealPlanCreateRequestModel implements IMealPlanCreateRequestModel {
    householdId: number = 0;
    authorId: number = 0;
    date: Date = new Date();
    mealTypeId: number = 0;
    title: string = '';
    notes?: string;
    recipeId?: number;

    constructor(data?: Partial<IMealPlanCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class MealPlanCreateResponseModel implements IMealPlanCreateResponseModel {
    id: number = 0;
    householdId: number = 0;
    authorId: number = 0;
    date: Date = new Date();
    mealTypeId: number = 0;
    title: string = '';
    notes?: string;
    recipeId?: number;
    createdDate: Date = new Date();

    constructor(data?: Partial<IMealPlanCreateResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class MealPlanUpdateRequestModel implements IMealPlanUpdateRequestModel {
    id: number = 0;
    householdId: number = 0;
    authorId: number = 0;
    date: Date = new Date();
    mealTypeId: number = 0;
    title: string = '';
    notes?: string;
    recipeId?: number;

    constructor(data?: Partial<IMealPlanUpdateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

// Export rule models from the separate file
export { MealPlanRuleModel, MealPlanRuleCreateRequestModel, MealPlanRuleCreateResponseModel, MealPlanRuleResponseModel } from './meal-plan-rule.model'; 