// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-create-response.class.ts

import { IMealPlanRuleCreateResponseModel } from './meal-plan-rule-create-response.model.interface';

export class MealPlanRuleCreateResponseModel implements IMealPlanRuleCreateResponseModel {
    id = 0;
    name = '';
    description = '';
    ruleTypeId = 0;
    householdId = 0;
    isActive = true;
} 