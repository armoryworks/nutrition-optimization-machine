// File: nom-ui/src/app/meal-plan/models/meal-plan.model.ts

import { IMealPlanModel } from './meal-plan.model.interface';


export class MealPlanModel implements IMealPlanModel {
    id = 0;
    householdId = 0;
    date: Date = new Date();
    mealTypeId = 0;
    mealType = '';
    title = '';
    notes?: string;
    description?: string;
    recipeId?: number;
    recipeName?: string;
    groupName?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    authorId = 0;
    createdById = 0;
    userId = 0;

    constructor(data?: Partial<IMealPlanModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 