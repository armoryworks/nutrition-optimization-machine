import { PlanParticipantModel } from './person.model';

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

export interface CreatePlanRequest {
  name: string;
  description: string | null;
  startDate: string;
  endDate: string | null;
  goals: GoalModel[];
  meals: MealModel[];
  restrictions: PlanRestrictionModel[];
}

export interface UpdatePlanRequest {
  name: string;
  description: string | null;
  startDate: string;
  endDate: string | null;
  goals: GoalModel[];
  meals: MealModel[];
  restrictions: PlanRestrictionModel[];
}

export interface GoalModel {
  id: number;
  name: string;
  description: string;
  goalType: string | null;
  beginDate: string | null;
  endDate: string | null;
  goalItems: GoalItemModel[];
}

export interface GoalItemModel {
  id: number;
  name: string;
  description: string;
  isQuantifiable: boolean;
  ingredientName: string | null;
  nutrientName: string | null;
  timeframeType: string | null;
  measurement: string | null;
  measurementMinimum: number | null;
  measurementMaximum: number | null;
}

export interface MealModel {
  id: number;
  mealType: string;
  date: string;
  recipes: MealRecipeModel[];
}

export interface MealRecipeModel {
  id: number;
  name: string;
  description: string | null;
  curationStatus: string;
}

export interface PlanRestrictionModel {
  id: number;
  name: string;
  description: string | null;
  restrictionType: string | null;
  ingredientName: string | null;
  nutrientName: string | null;
}

export interface ClonePlanRequest {
  sourcePlanId: number;
  newPlanName: string;
}
