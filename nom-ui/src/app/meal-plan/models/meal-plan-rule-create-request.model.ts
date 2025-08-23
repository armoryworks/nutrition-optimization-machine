// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-create-request.class.ts

import { IMealPlanRuleCreateRequestModel } from './meal-plan-rule-create-request.model.interface';

export class MealPlanRuleCreateRequestModel {
    name = '';
    description = '';
    ruleTypeId = 0;
    householdId = 0;
    isActive = true;
} 