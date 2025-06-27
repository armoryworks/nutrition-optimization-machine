// nom-ui/src/app/admin/models/recipe-import-request.model.ts

/**
 * Represents the request payload for initiating a recipe import job on the backend.
 */
export interface RecipeImportRequestModel {
  /**
   * The file path to the CSV file containing recipe data on the server where the backend is running.
   */
  sourceFilePath: string;
}
