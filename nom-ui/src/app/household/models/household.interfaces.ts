// File: nom-ui/src/app/household/models/household.interfaces.ts

export interface IHouseholdModel {
    id: number;
    name: string;
    description?: string;
    createdDate: Date;
    modifiedDate?: Date;
}

export interface IHouseholdCreateRequestModel {
    name: string;
    description?: string;
}

export interface IHouseholdCreateResponseModel {
    id: number;
    name: string;
    description?: string;
    createdDate: Date;
    inviteToken: string;
}

export interface IHouseholdResponseModel {
    id: number;
    name: string;
    description?: string;
    createdDate: Date;
    modifiedDate?: Date;
    memberCount: number;
    isOwner: boolean;
}

export interface IHouseholdUpdateRequestModel {
    name: string;
    description?: string;
}

export interface IHouseholdInviteTokenCreateRequestModel {
    householdId: number;
    expiresAt?: Date;
}

export interface IHouseholdInviteTokenResponseModel {
    id: number;
    householdId: number;
    token: string;
    expiresAt?: Date;
    createdDate: Date;
    usesLeft?: number;
}

export interface IHouseholdMemberCreateRequestModel {
    householdId: number;
    personId: number;
}

export interface IHouseholdMemberResponseModel {
    id: number;
    householdId: number;
    personId: number;
    personName: string;
    joinedDate: Date;
    isOwner: boolean;
} 