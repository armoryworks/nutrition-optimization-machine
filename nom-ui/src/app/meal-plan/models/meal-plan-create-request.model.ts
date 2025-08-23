// File: nom-ui/src/app/meal-plan/models/meal-plan-create-request.class.ts

import { IMealPlanCreateRequestModel } from './meal-plan-create-request.model.interface';

export class MealPlanCreateRequestModel implements IMealPlanCreateRequestModel {
    householdId = 0;
    date: Date = new Date();
    mealTypeId = 0;
    title = '';
    notes?: string;
    recipeId?: number;

    constructor(data?: Partial<IMealPlanCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 