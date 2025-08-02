// File: nom-ui/src/app/household/models/household-update-request.model.ts

import { IHouseholdUpdateRequestModel } from './household-update-request.model.interface';

export class HouseholdUpdateRequestModel implements IHouseholdUpdateRequestModel {
    name: string = '';
    description?: string;

    constructor(data?: Partial<IHouseholdUpdateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 