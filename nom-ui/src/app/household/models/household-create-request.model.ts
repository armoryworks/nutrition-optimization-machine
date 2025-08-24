// File: nom-ui/src/app/household/models/household-create-request.model.ts

import { IHouseholdCreateRequestModel } from './household-create-request.model.interface';

export class HouseholdCreateRequestModel implements IHouseholdCreateRequestModel {
    name = '';
    description?: string;
    groupId = 3; // Temporary: Using Recipe Type group ID (3) to fix foreign key constraint

    constructor(data?: Partial<IHouseholdCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 