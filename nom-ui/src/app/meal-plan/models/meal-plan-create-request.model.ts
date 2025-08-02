// File: nom-ui/src/app/meal-plan/models/meal-plan-create-request.class.ts

import { IMealPlanCreateRequestModel } from './meal-plan-create-request.model.interface';

export class MealPlanCreateRequestModel implements IMealPlanCreateRequestModel {
    householdId: number = 0;
    date: Date = new Date();
    mealTypeId: number = 0;
    mealType?: string;
    title: string = '';
    notes?: string;
    recipeId?: number;

    constructor(data?: Partial<IMealPlanCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 