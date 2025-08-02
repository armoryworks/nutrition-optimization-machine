// File: nom-ui/src/app/household/models/household.model.interface.ts

export interface IHouseholdModel {
    id: number;
    name: string;
    description?: string;
    createdDate: Date;
    modifiedDate?: Date;
    memberCount: number;
    planCount: number;
} 