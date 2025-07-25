import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface RecipeModel extends BaseCommonModel {
    name: string;
    description?: string;
    instructions?: string;
    prepTimeMinutes?: number;
    cookTimeMinutes?: number;
    servings?: number;
    servingQuantity?: number;
    servingQuantityMeasurementTypeId?: number;
    authorId: number;
    curationStatusId: number;
    version: number;
    parentRecipeId?: number;
    sourceUrl?: string;
    sourceSite?: string;
}