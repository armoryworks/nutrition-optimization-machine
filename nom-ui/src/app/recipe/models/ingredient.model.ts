import { NutrientValueModel } from '../../nutrient/models/nutrient-value.model';
import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface IngredientModel extends BaseCommonModel {
    name: string;
    description?: string;
    fdcId?: string;
    curationStatusId: number;
    authorId?: number;
    nutrients: NutrientValueModel[];
}