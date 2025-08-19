// File: nom-ui/src/app/household/models/household-member-create-request.class.ts

import { IHouseholdMemberCreateRequestModel } from './household-member-create-request.interface';

export class HouseholdMemberCreateRequestModel implements IHouseholdMemberCreateRequestModel {
    householdId = 0;
    personId = 0;

    constructor(data?: Partial<IHouseholdMemberCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 