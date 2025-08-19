// File: nom-ui/src/app/household/models/household-invite-token-create-request.class.ts

import { IHouseholdInviteTokenCreateRequestModel } from './household-invite-token-create-request.interface';

export class HouseholdInviteTokenCreateRequestModel implements IHouseholdInviteTokenCreateRequestModel {
    householdId = 0;
    expiresAt?: Date;

    constructor(data?: Partial<IHouseholdInviteTokenCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 