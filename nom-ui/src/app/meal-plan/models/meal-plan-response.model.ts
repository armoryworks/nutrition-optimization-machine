// File: nom-ui/src/app/meal-plan/models/meal-plan-response.class.ts

import { IMealPlanResponseModel } from './meal-plan-response.model.interface';

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