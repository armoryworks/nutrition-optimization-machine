// File: nom-ui/src/app/meal-plan/models/meal-plan-rule-response.class.ts

import { IMealPlanRuleResponseModel } from './meal-plan-rule-response.model.interface';

export class MealPlanRuleResponseModel implements IMealPlanRuleResponseModel {
    id = 0;
    name = '';
    description = '';
    ruleTypeId = 0;
    householdId = 0;
    isActive = true;
} 