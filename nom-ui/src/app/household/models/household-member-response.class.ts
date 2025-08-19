// File: nom-ui/src/app/household/models/household-member-response.class.ts

import { IHouseholdMemberResponseModel } from './household-member-response.interface';

export class HouseholdMemberResponseModel implements IHouseholdMemberResponseModel {
    id = 0;
    householdId = 0;
    personId = 0;
    personName = '';
    joinedDate: Date = new Date();
    isOwner = false;

    constructor(data?: Partial<IHouseholdMemberResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 