// File: nom-ui/src/app/meal-plan/models/meal-plan.classes.ts

import {
    IMealPlanModel,
    IMealPlanCreateRequestModel,
    IMealPlanCreateResponseModel,
    IMealPlanResponseModel,
    IMealPlanUpdateRequestModel,
    IMealPlanRuleModel,
    IMealPlanRuleCreateRequestModel,
    IMealPlanRuleCreateResponseModel,
    IMealPlanRuleResponseModel
} from './meal-plan.interfaces';

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

export class MealPlanCreateRequestModel implements IMealPlanCreateRequestModel {
    householdId: number = 0;
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

export class MealPlanResponseModel implements IMealPlanResponseModel {
    id: number = 0;
    householdId: number = 0;
    authorId: number = 0;
    date: Date = new Date();
    mealTypeId: number = 0;
    mealType: string = '';
    title: string = '';
    notes?: string;
    recipeId?: number;
    recipeName?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;

    constructor(data?: Partial<IMealPlanResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class MealPlanUpdateRequestModel implements IMealPlanUpdateRequestModel {
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

export class MealPlanRuleCreateRequestModel implements IMealPlanRuleCreateRequestModel {
    householdId: number = 0;
    dayOfWeekId: number = 0;
    mealTypeId: number = 0;
    queryFilterString?: string;

    constructor(data?: Partial<IMealPlanRuleCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

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