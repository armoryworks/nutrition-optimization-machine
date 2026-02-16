// File: nom-ui/src/app/household/models/household-response.model.interface.ts

import { IHouseholdMemberResponseModel } from './household-member-response.model.interface';

export interface IHouseholdResponseModel {
    id: number;
    name: string;
    description?: string;
    createdDate: Date;
    modifiedDate?: Date;
    memberCount: number;
    planCount: number;
    isOwner: boolean;
    householdGroupId?: number;
    inviteToken?: string;
    members?: IHouseholdMemberResponseModel[];
    recipeCount?: number;
    shoppingListCount?: number;
} 