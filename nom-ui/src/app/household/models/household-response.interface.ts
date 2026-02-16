// File: nom-ui/src/app/household/models/household-response.interface.ts

export interface IHouseholdResponseModel {
    id: number;
    name: string;
    description?: string;
    createdDate: Date;
    modifiedDate?: Date;
    memberCount: number;
    planCount: number;
    isOwner: boolean;
    householdGroupId?: number;
} 