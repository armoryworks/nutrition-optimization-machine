// File: nom-ui/src/app/meal-plan/models/meal-plan-response.class.ts

import { IMealPlanResponseModel } from './meal-plan-response.model.interface';

export class MealPlanResponseModel implements IMealPlanResponseModel {
    id = 0;
    householdId = 0;
    date: Date = new Date();
    mealTypeId = 0;
    title = '';
    notes?: string;
    recipeId?: number;
    createdDate: Date = new Date();
    modifiedDate?: Date;
} 