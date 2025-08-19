// File: nom-ui/src/app/household/models/household-model.class.ts

import { IHouseholdModel } from './household-model.interface';

export class HouseholdModel implements IHouseholdModel {
    id = 0;
    name = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    memberCount = 0;
    planCount = 0;

    constructor(data?: Partial<IHouseholdModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 