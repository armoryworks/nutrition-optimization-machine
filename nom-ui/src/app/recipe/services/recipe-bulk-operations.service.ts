import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export enum ExportTypes {
    Json = 'Json',
    Csv = 'Csv',
    Pdf = 'Pdf'
}

export interface RecipeBulkExportRequest {
    recipeIds: number[];
    exportType: ExportTypes;
    includeImages?: boolean;
    includeMetadata?: boolean;
}

export interface RecipeBulkAssignCategoriesRequest {
    recipeIds: number[];
    categories: string[];
}

export interface RecipeBulkAssignTagsRequest {
    recipeIds: number[];
    tags: string[];
}

export interface RecipeBulkUpdateSettingsRequest {
    recipeIds: number[];
    isPublic?: boolean;
    isArchived?: boolean;
    curationStatus?: string;
    notes?: string;
}

export interface RecipeBulkDeleteRequest {
    recipeIds: number[];
    permanent: boolean;
}

export interface RecipeBulkImportRequest {
    file: File;
    importType: ExportTypes;
    overwriteExisting?: boolean;
    defaultCategories?: string[];
    defaultTags?: string[];
}

export interface RecipeBulkOperationResponse {
    success: boolean;
    message: string;
    processedCount: number;
    successCount: number;
    errorCount: number;
    errors: string[];
    downloadUrl?: string;
    exportId?: number;
}

export interface RecipeBulkOperationProgress {
    operationId: number;
    operationType: string;
    totalItems: number;
    processedItems: number;
    successItems: number;
    errorItems: number;
    status: string; // Pending, InProgress, Completed, Failed
    startTime: Date;
    endTime?: Date;
    errors: string[];
}

export interface RecipeExportFile {
    exportId: number;
    fileName: string;
    filePath: string;
    fileSize: number;
    contentType: string;
    createdDate: Date;
    expiryDate: Date;
    recipeCount: number;
    exportType: ExportTypes;
}

@Injectable({
    providedIn: 'root'
})
export class RecipeBulkOperationsService {
    private http = inject(HttpClient);

    private readonly apiUrl = `${environment.apiUrl}/RecipeBulkOperations`;



    /**
     * Export recipes to file
     */
    exportRecipes(request: RecipeBulkExportRequest): Observable<RecipeBulkOperationResponse> {
        return this.http.post<RecipeBulkOperationResponse>(`${this.apiUrl}/export`, request);
    }

    /**
     * Import recipes from file
     */
    importRecipes(request: RecipeBulkImportRequest): Observable<RecipeBulkOperationResponse> {
        const formData = new FormData();
        formData.append('file', request.file);
        formData.append('importType', request.importType.toString());
        formData.append('overwriteExisting', request.overwriteExisting?.toString() || 'false');

        if (request.defaultCategories) {
            formData.append('defaultCategories', JSON.stringify(request.defaultCategories));
        }

        if (request.defaultTags) {
            formData.append('defaultTags', JSON.stringify(request.defaultTags));
        }

        return this.http.post<RecipeBulkOperationResponse>(`${this.apiUrl}/import`, formData);
    }

    /**
     * Assign categories to recipes
     */
    assignCategories(request: RecipeBulkAssignCategoriesRequest): Observable<RecipeBulkOperationResponse> {
        return this.http.post<RecipeBulkOperationResponse>(`${this.apiUrl}/assign-categories`, request);
    }

    /**
     * Assign tags to recipes
     */
    assignTags(request: RecipeBulkAssignTagsRequest): Observable<RecipeBulkOperationResponse> {
        return this.http.post<RecipeBulkOperationResponse>(`${this.apiUrl}/assign-tags`, request);
    }

    /**
     * Update settings for recipes
     */
    updateSettings(request: RecipeBulkUpdateSettingsRequest): Observable<RecipeBulkOperationResponse> {
        return this.http.post<RecipeBulkOperationResponse>(`${this.apiUrl}/update-settings`, request);
    }

    /**
     * Delete recipes
     */
    deleteRecipes(request: RecipeBulkDeleteRequest): Observable<RecipeBulkOperationResponse> {
        return this.http.post<RecipeBulkOperationResponse>(`${this.apiUrl}/delete`, request);
    }

    /**
     * Get bulk operation progress
     */
    getOperationProgress(operationId: number): Observable<RecipeBulkOperationProgress> {
        return this.http.get<RecipeBulkOperationProgress>(`${this.apiUrl}/progress/${operationId}`);
    }

    /**
     * Get all export files for the current user
     */
    getExportFiles(): Observable<RecipeExportFile[]> {
        return this.http.get<RecipeExportFile[]>(`${this.apiUrl}/exports`);
    }

    /**
     * Get export file by ID
     */
    getExportFile(exportId: number): Observable<RecipeExportFile> {
        return this.http.get<RecipeExportFile>(`${this.apiUrl}/exports/${exportId}`);
    }

    /**
     * Download export file
     */
    downloadExportFile(exportId: number): Observable<Blob> {
        return this.http.get(`${this.apiUrl}/download/${exportId}`, { responseType: 'blob' });
    }

    /**
     * Delete export file
     */
    deleteExportFile(exportId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/exports/${exportId}`);
    }

    /**
     * Clean up expired export files
     */
    cleanupExpiredExports(): Observable<{ deletedCount: number }> {
        return this.http.post<{ deletedCount: number }>(`${this.apiUrl}/cleanup-exports`, {});
    }

    /**
     * Helper method to download a file
     */
    downloadFile(blob: Blob, fileName: string): void {
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.URL.revokeObjectURL(url);
    }
} 