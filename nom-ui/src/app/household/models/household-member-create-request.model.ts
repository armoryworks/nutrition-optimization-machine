// File: nom-ui/src/app/household/models/household-member-create-request.model.ts

import { IHouseholdMemberCreateRequestModel } from './household-member-create-request.model.interface';

export class HouseholdMemberCreateRequestModel implements IHouseholdMemberCreateRequestModel {
    householdId: number = 0;
    personId: number = 0;

    constructor(data?: Partial<IHouseholdMemberCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 