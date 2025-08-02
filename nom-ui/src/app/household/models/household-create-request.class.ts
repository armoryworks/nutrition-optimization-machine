// File: nom-ui/src/app/household/models/household-create-request.class.ts

import { IHouseholdCreateRequestModel } from './household-create-request.interface';

export class HouseholdCreateRequestModel implements IHouseholdCreateRequestModel {
    name: string = '';
    description?: string;

    constructor(data?: Partial<IHouseholdCreateRequestModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 