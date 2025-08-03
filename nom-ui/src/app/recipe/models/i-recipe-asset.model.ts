import { BaseCommonModel } from '../../common/models/_base-common.model';

export interface RecipeAssetModel extends BaseCommonModel {
    name: string;
    icon: string;
    description?: string;
    fileName: string;
    fileSize: number;
    mimeType: string;
    filePath?: string;
    recipeId: number;
    uploadedBy: number;
    isPublic: boolean;
} 