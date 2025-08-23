// File: nom-ui/src/app/meal-plan/models/meal-plan.model.ts

import { IMealPlanModel } from './meal-plan.model.interface';


export class MealPlanModel implements IMealPlanModel {
    id = 0;
    householdId = 0;
    date: Date = new Date();
    mealTypeId = 0;
    title = '';
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