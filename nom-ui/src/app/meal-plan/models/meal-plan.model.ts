// File: nom-ui/src/app/meal-plan/models/meal-plan-model.class.ts

import { IMealPlanModel } from './meal-plan.model.interface';

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