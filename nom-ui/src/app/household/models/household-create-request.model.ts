// File: nom-ui/src/app/household/models/household-create-request.model.ts

import { IHouseholdCreateRequestModel } from './household-create-request.model.interface';

export class HouseholdCreateRequestModel implements IHouseholdCreateRequestModel {
    name = '';
    description?: string;
    householdGroupId = 1; // Default household group

    constructor(data?: Partial<IHouseholdCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 