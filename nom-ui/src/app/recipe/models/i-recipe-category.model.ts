import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface RecipeCategoryModel extends BaseCommonModel {
    name: string;
    description?: string;
    icon: string;
    color: string;
    slug: string;
    recipeCount: number;
    isPublic: boolean;
    createdBy: number;
    householdId?: number;
    parentCategoryId?: number;
    parentCategory?: RecipeCategoryModel;
    childCategories?: RecipeCategoryModel[];
} 