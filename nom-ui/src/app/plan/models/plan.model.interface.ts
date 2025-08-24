export interface IPlanModel {
    id: number;
    name: string;
    description?: string;
    startDate?: Date;
    endDate?: Date;
    invitationCode?: string;
    curationStatus: string;
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
