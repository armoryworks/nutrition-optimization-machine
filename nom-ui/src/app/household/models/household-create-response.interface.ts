// File: nom-ui/src/app/household/models/household-create-response.interface.ts

export interface IHouseholdCreateResponseModel {
    id: number;
    name: string;
    description?: string;
    createdDate: Date;
    inviteToken: string;
} 