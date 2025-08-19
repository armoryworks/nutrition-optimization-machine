// File: nom-ui/src/app/meal-plan/models/meal-plan-update-request.class.ts

import { IMealPlanUpdateRequestModel } from './meal-plan-update-request.model.interface';

export class MealPlanUpdateRequestModel implements IMealPlanUpdateRequestModel {
    date: Date = new Date();
    mealTypeId = 0;
    title = '';
    notes?: string;
    recipeId?: number;

    constructor(data?: Partial<IMealPlanUpdateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 