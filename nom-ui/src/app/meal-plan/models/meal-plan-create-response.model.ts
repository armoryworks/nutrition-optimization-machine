// File: nom-ui/src/app/meal-plan/models/meal-plan-create-response.class.ts

import { IMealPlanCreateResponseModel } from './meal-plan-create-response.model.interface';

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