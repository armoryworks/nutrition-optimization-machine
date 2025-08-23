export interface IPlanModel {
    id: number;
    name: string;
    description?: string;
    startDate: Date;
    endDate?: Date;
    goals: GoalModel[];
    meals: MealModel[];
    restrictions: RestrictionModel[];
    createdDate: Date;
    modifiedDate?: Date;
}
