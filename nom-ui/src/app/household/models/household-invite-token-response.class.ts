// File: nom-ui/src/app/household/models/household-invite-token-response.class.ts

import { IHouseholdInviteTokenResponseModel } from './household-invite-token-response.interface';

export class HouseholdInviteTokenResponseModel implements IHouseholdInviteTokenResponseModel {
    id = 0;
    householdId = 0;
    token = '';
    expiresAt?: Date;
    createdDate: Date = new Date();
    usesLeft?: number;

    constructor(data?: Partial<IHouseholdInviteTokenResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 