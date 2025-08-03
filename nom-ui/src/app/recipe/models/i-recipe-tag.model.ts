import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface RecipeTagModel extends BaseCommonModel {
    name: string;
    description?: string;
    color: string;
    slug: string;
    recipeCount: number;
    isPublic: boolean;
    createdBy: number;
    householdId?: number;
} 