// File: nom-ui/src/app/household/models/household-invite-token-response.interface.ts

export interface IHouseholdInviteTokenResponseModel {
    id: number;
    householdId: number;
    token: string;
    expiresAt?: Date;
    createdDate: Date;
    usesLeft?: number;
} 