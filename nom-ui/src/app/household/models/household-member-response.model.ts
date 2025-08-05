// File: nom-ui/src/app/household/models/household-member-response.model.ts

import { IHouseholdMemberResponseModel } from './household-member-response.model.interface';

export class HouseholdMemberResponseModel implements IHouseholdMemberResponseModel {
    id: number = 0;
    householdId: number = 0;
    personId: number = 0;
    personName: string = '';
    joinedDate: Date = new Date();
    isOwner: boolean = false;
    name?: string;
    email?: string;
    role?: string;

    constructor(data?: Partial<IHouseholdMemberResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 