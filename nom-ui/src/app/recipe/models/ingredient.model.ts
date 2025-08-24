import { NutrientValueModel } from '../../nutrient/models/nutrient-value.model';
import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface IngredientModel extends BaseCommonModel {
    name: string;
    description?: string;
    categoryId?: number;
    fdcId?: string;
    curationStatusId: number;
    nutrients: NutrientValueModel[];
    allergens: number[];
    isActive: boolean;
    authorId: number;
    createdById: number;
    userId: number;
}