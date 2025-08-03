import { Injectable } from '@angular/core';
import { HttpClient, HttpEvent, HttpEventType } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { RecipeAssetModel } from '../models/i-recipe-asset.model';

export interface RecipeAssetCreateData {
    name: string;
    icon: string;
    description?: string;
    fileName: string;
    fileSize: number;
    mimeType: string;
}

@Injectable({
    providedIn: 'root'
})
export class RecipeAssetsService {
    private readonly baseUrl = `${environment.apiUrl}/recipe`;

    constructor(private http: HttpClient) { }

    getRecipeAssets(recipeId: number): Observable<RecipeAssetModel[]> {
        return this.http.get<RecipeAssetModel[]>(`${this.baseUrl}/${recipeId}/assets`);
    }

    createRecipeAsset(recipeId: number, assetData: RecipeAssetCreateData, file: File): Observable<RecipeAssetModel> {
        const formData = new FormData();
        formData.append('file', file);
        formData.append('name', assetData.name);
        formData.append('icon', assetData.icon);
        if (assetData.description) {
            formData.append('description', assetData.description);
        }
        formData.append('fileName', assetData.fileName);
        formData.append('fileSize', assetData.fileSize.toString());
        formData.append('mimeType', assetData.mimeType);

        return this.http.post<RecipeAssetModel>(`${this.baseUrl}/${recipeId}/assets`, formData);
    }

    deleteRecipeAsset(assetId: number): Observable<void> {
        return this.http.delete<void>(`${this.baseUrl}/assets/${assetId}`);
    }

    downloadAsset(assetId: number): Observable<Blob> {
        return this.http.get(`${this.baseUrl}/assets/${assetId}/download`, {
            responseType: 'blob'
        });
    }

    getAssetUrl(assetId: number): string {
        return `${this.baseUrl}/assets/${assetId}/download`;
    }

    uploadWithProgress(recipeId: number, assetData: RecipeAssetCreateData, file: File): Observable<{ progress: number; asset?: RecipeAssetModel }> {
        const formData = new FormData();
        formData.append('file', file);
        formData.append('name', assetData.name);
        formData.append('icon', assetData.icon);
        if (assetData.description) {
            formData.append('description', assetData.description);
        }
        formData.append('fileName', assetData.fileName);
        formData.append('fileSize', assetData.fileSize.toString());
        formData.append('mimeType', assetData.mimeType);

        return this.http.post<RecipeAssetModel>(`${this.baseUrl}/${recipeId}/assets`, formData, {
            reportProgress: true,
            observe: 'events'
        }).pipe(
            map((event: HttpEvent<any>) => {
                switch (event.type) {
                    case HttpEventType.UploadProgress:
                        const progress = Math.round(100 * event.loaded / (event.total || 1));
                        return { progress };
                    case HttpEventType.Response:
                        return { progress: 100, asset: event.body };
                    default:
                        return { progress: 0 };
                }
            })
        );
    }
} 