

/**
 * Interface representing the structure of a nutritional plan.
 * Used on the frontend for data transfer and display.
 */
export interface PlanModel {
  id: number;
  name: string;
  description?: string;
  startDate?: Date;
  endDate?: Date;
  invitationCode?: string;
  curationStatus: string;
  authorId: number;
  authorName: string;
  dateSubmittedForCuration?: Date;
  dateCurationCompleted?: Date;
  parentPlanId?: number;
  version: number;
  createdDate: Date;
  lastModifiedDate?: Date;

  // Navigation properties
  goals: GoalModel[];
  meals: MealModel[];
  restrictions: RestrictionModel[];
  participants?: PlanParticipantModel[];
}

export interface GoalModel {
  id: number;
  name: string;
  description?: string;
  goalType?: string;
  beginDate?: Date;
  endDate?: Date;
  goalItems: GoalItemModel[];
}

export interface GoalItemModel {
  id: number;
  name: string;
  description?: string;
  isQuantifiable: boolean;
  ingredientName?: string;
  nutrientName?: string;
  timeframeType?: string;
  measurementType?: string;
  measurementMinimum?: number;
  measurementMaximum?: number;
}

export interface MealModel {
  id: number;
  mealType: string;
  date: Date;
  recipes: RecipeModel[];
}

export interface RecipeModel {
  id: number;
  name: string;
  description?: string;
  curationStatus: string;
}

export interface RestrictionModel {
  id: number;
  name: string;
  description?: string;
  restrictionType?: string;
  ingredientName?: string;
  nutrientName?: string;
}

export interface PlanParticipantModel {
  id: number;
  personId: number;
  personName: string;
  role: string;
  joinedDate: Date;
}

export interface CreatePlanRequest {
  name: string;
  description?: string;
  startDate?: Date;
  endDate?: Date;
  goals?: GoalModel[];
  meals?: MealModel[];
  restrictions?: RestrictionModel[];
}

export interface UpdatePlanRequest {
  name: string;
  description?: string;
  startDate?: Date;
  endDate?: Date;
}

export interface ClonePlanRequest {
  newPlanName: string;
}
