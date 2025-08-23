// File: nom-ui/src/app/meal-plan/models/meal-plan-create-response.class.ts

import { IMealPlanCreateResponseModel } from './meal-plan-create-response.model.interface';

export class MealPlanCreateResponseModel implements IMealPlanCreateResponseModel {
    id = 0;
    householdId = 0;
    date: Date = new Date();
    mealTypeId = 0;
    title = '';
    notes?: string;
    recipeId?: number;
    createdDate: Date = new Date();

    constructor(data?: Partial<IMealPlanCreateResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 