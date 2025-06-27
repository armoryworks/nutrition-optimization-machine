// nom-ui/src/app/admin/models/recipe-import-response.model.ts
import { ApiResponseCommonModel } from '../../common/models/api-response-common.model';

/**
 * Represents the response received after initiating a recipe import job.
 * Extends ApiResponseCommonModel to include common success/message properties.
 */
export interface RecipeImportResponseModel extends ApiResponseCommonModel {
  /**
   * The unique identifier for the initiated import process.
   */
  processId: string; // GUID as a string
}
