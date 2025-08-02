// File: nom-ui/src/app/household/models/household-update-request.class.ts

import { IHouseholdUpdateRequestModel } from './household-update-request.interface';

export class HouseholdUpdateRequestModel implements IHouseholdUpdateRequestModel {
    name: string = '';
    description?: string;

    constructor(data?: Partial<IHouseholdUpdateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 