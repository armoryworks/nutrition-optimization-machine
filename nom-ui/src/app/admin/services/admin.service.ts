// nom-ui/src/app/admin/services/admin.service.ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { RecipeImportRequestModel } from '../models/recipe-import-request.model';
import { RecipeImportResponseModel } from '../models/recipe-import-response.model';
import { ImportJobStatusResponseModel } from '../models/import-job-status-response.model';

@Injectable({
  providedIn: 'root',
})
export class AdminService {
  private apiUrl = '/api/RecipeAdmin';

  constructor(private http: HttpClient) {}

  /**
   * Initiates a recipe import job on the backend.
   * @param request The request containing the source file path.
   * @returns An Observable of the import initiation response.
   */
  initiateRecipeImport(
    request: RecipeImportRequestModel
  ): Observable<RecipeImportResponseModel> {
    const url = `${this.apiUrl}/Recipes/import`;
    console.log(`Sending POST request to ${url} with payload:`, request);
    return this.http.post<RecipeImportResponseModel>(url, request);
  }

  /**
   * Retrieves the current status of a specific recipe import job.
   * @param processId The unique ID (GUID as string) of the import job.
   * @returns An Observable of the import job status response.
   */
  getImportStatus(processId: string): Observable<ImportJobStatusResponseModel> {
    const url = `${this.apiUrl}/Recipes/import/${processId}/status`;
    console.log(`Sending GET request to ${url}`);
    return this.http.get<ImportJobStatusResponseModel>(url);
  }

  /**
   * Sends a file to the backend for recipe import.
   * You MUST implement this method in your AdminService.
   * The backend endpoint needs to be able to accept a file via FormData.
   */
  initiateRecipeImportWithFile(
    file: File,
    jobName: string
  ): Observable<RecipeImportResponseModel> {
    const formData = new FormData();
    formData.append('File', file, file.name); // 'File' must match the property name in RecipeImportFromFileRequestModel
    formData.append('JobName', jobName); // 'JobName' must match the property name in RecipeImportFromFileRequestModel

    // The endpoint should be '/api/RecipeAdmin/Recipes/import-from-file'
    return this.http.post<RecipeImportResponseModel>(
      '/api/RecipeAdmin/Recipes/import-from-file',
      formData
    );
  }
}
