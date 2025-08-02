// File: nom-ui/src/app/household/models/household-model.class.ts

import { IHouseholdModel } from './household-model.interface';

export class HouseholdModel implements IHouseholdModel {
    id: number = 0;
    name: string = '';
    description?: string;
    createdDate: Date = new Date();
    modifiedDate?: Date;
    memberCount: number = 0;
    planCount: number = 0;

    constructor(data?: Partial<IHouseholdModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 