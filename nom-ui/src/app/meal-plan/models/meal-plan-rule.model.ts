// File: nom-ui/src/app/meal-plan/models/meal-plan-rule.model.ts

import { IMealPlanRuleModel } from './meal-plan-rule.model.interface';

export class MealPlanRuleModel implements IMealPlanRuleModel {
    id = 0;
    name = '';
    description = '';
    ruleTypeId = 0;
    householdId = 0;
    isActive = true;
}

