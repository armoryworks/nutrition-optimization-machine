// File: nom-ui/src/app/household/models/household-response.model.ts

import { IHouseholdResponseModel } from './household-response.model.interface';

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