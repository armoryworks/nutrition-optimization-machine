// File: nom-ui/src/app/household/models/household-member-response.interface.ts

export interface IHouseholdMemberResponseModel {
    id: number;
    householdId: number;
    personId: number;
    personName: string;
    joinedDate: Date;
    isOwner: boolean;
} 