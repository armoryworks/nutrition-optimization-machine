import { NutrientValueModel } from '../../nutrient/models/nutrient-value.model';
import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface IngredientModel extends BaseCommonModel {
    name: string;
    description?: string;
    categoryId?: number;
    fdcId?: string;
    curationStatus?: string; // Backend returns this as a string, not curationStatusId as number
    curationStatusId?: number; // Keep for backward compatibility, but backend doesn't return this
    nutrients: NutrientValueModel[];
    allergens?: number[];
    isActive?: boolean;
    authorId: number;
    createdById?: number;
    userId?: number;
}