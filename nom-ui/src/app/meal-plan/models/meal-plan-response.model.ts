// File: nom-ui/src/app/meal-plan/models/meal-plan-response.class.ts

import { IMealPlanResponseModel } from './meal-plan-response.model.interface';

export class MealPlanResponseModel implements IMealPlanResponseModel {
    id = 0;
    householdId = 0;
    authorId = 0;
    date: Date = new Date();
    mealTypeId = 0;
    mealType = '';
    title = '';
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