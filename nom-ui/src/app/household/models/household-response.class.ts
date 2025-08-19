// File: nom-ui/src/app/household/models/household-response.class.ts

import { IHouseholdResponseModel } from './household-response.interface';

export class HouseholdResponseModel implements IHouseholdResponseModel {
    id = 0;
    name = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    memberCount = 0;
    planCount = 0;
    isOwner = false;
    groupId?: number;

    constructor(data?: Partial<IHouseholdResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 