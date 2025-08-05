// File: nom-ui/src/app/household/models/household-response.model.ts

import { IHouseholdResponseModel } from './household-response.model.interface';
import { HouseholdMemberResponseModel } from './household-member-response.model';

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
    inviteToken?: string;
    members?: HouseholdMemberResponseModel[];
    recipeCount?: number;
    shoppingListCount?: number;

    constructor(data?: Partial<IHouseholdResponseModel>) {
        if (data) {
            Object.assign(this, data);
        }
    }
} 