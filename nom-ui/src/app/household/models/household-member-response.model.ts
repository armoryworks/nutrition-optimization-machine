// File: nom-ui/src/app/household/models/household-member-response.model.ts

import { IHouseholdMemberResponseModel } from './household-member-response.model.interface';

export class HouseholdMemberResponseModel implements IHouseholdMemberResponseModel {
    id = 0;
    householdId = 0;
    personId = 0;
    personName = '';
    joinedDate: Date = new Date();
    isOwner = false;
    name?: string;
    email?: string;
    role?: string;

    constructor(data?: Partial<IHouseholdMemberResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 