// File: nom-ui/src/app/household/models/household-create-response.model.ts

import { IHouseholdCreateResponseModel } from './household-create-response.model.interface';

export class HouseholdCreateResponseModel implements IHouseholdCreateResponseModel {
    id = 0;
    name = '';
    description?: string;
    createdDate: Date = new Date();
    inviteToken = '';

    constructor(data?: Partial<IHouseholdCreateResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 