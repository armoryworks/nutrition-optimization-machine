import { GoalModel } from './goal.model';
import { MealModel } from './meal.model';
import { PlanRestrictionModel } from './plan-restriction.model';
import { PlanParticipantModel } from './plan-participant.model';

export interface PlanModel {
  id: number;
  name: string;
  description: string | null;
  startDate: string;
  endDate: string | null;
  invitationCode: string | null;
  curationStatus: string;
  authorId: number;
  authorName: string;
  createdDate: string;
  lastModifiedDate: string | null;
  goals: GoalModel[];
  meals: MealModel[];
  restrictions: PlanRestrictionModel[];
  participants: PlanParticipantModel[];
}
