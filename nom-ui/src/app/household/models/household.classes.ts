// File: nom-ui/src/app/household/models/household.classes.ts

import {
    IHouseholdModel,
    IHouseholdCreateRequestModel,
    IHouseholdCreateResponseModel,
    IHouseholdResponseModel,
    IHouseholdUpdateRequestModel,
    IHouseholdInviteTokenCreateRequestModel,
    IHouseholdInviteTokenResponseModel,
    IHouseholdMemberCreateRequestModel,
    IHouseholdMemberResponseModel
} from './household.interfaces';

export class HouseholdModel implements IHouseholdModel {
    id: number = 0;
    name: string = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    memberCount: number = 0;
    planCount: number = 0;

    constructor(data?: Partial<IHouseholdModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class HouseholdCreateRequestModel implements IHouseholdCreateRequestModel {
    name: string = '';
    description?: string;

    constructor(data?: Partial<IHouseholdCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class HouseholdCreateResponseModel implements IHouseholdCreateResponseModel {
    id: number = 0;
    name: string = '';
    description?: string;
    createdDate: Date = new Date();
    inviteToken: string = '';

    constructor(data?: Partial<IHouseholdCreateResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class HouseholdResponseModel implements IHouseholdResponseModel {
    id: number = 0;
    name: string = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    memberCount: number = 0;
    planCount: number = 0;
    isOwner: boolean = false;
    groupId?: number;

    constructor(data?: Partial<IHouseholdResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class HouseholdUpdateRequestModel implements IHouseholdUpdateRequestModel {
    name: string = '';
    description?: string;

    constructor(data?: Partial<IHouseholdUpdateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class HouseholdInviteTokenCreateRequestModel implements IHouseholdInviteTokenCreateRequestModel {
    householdId: number = 0;
    expiresAt?: Date;

    constructor(data?: Partial<IHouseholdInviteTokenCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class HouseholdInviteTokenResponseModel implements IHouseholdInviteTokenResponseModel {
    id: number = 0;
    householdId: number = 0;
    token: string = '';
    expiresAt?: Date;
    createdDate: Date = new Date();
    usesLeft?: number;

    constructor(data?: Partial<IHouseholdInviteTokenResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class HouseholdMemberCreateRequestModel implements IHouseholdMemberCreateRequestModel {
    householdId: number = 0;
    personId: number = 0;

    constructor(data?: Partial<IHouseholdMemberCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
}

export class HouseholdMemberResponseModel implements IHouseholdMemberResponseModel {
    id: number = 0;
    householdId: number = 0;
    personId: number = 0;
    personName: string = '';
    joinedDate: Date = new Date();
    isOwner: boolean = false;

    constructor(data?: Partial<IHouseholdMemberResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 