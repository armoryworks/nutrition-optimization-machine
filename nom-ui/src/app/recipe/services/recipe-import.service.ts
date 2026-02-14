import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecipeScrapeTestResult, RecipeCreateResponse } from '../models/recipe-import.model';

@Injectable({
    providedIn: 'root'
})
export class RecipeImportService {
    private http = inject(HttpClient);

    private readonly apiUrl = `${environment.apiUrl}/recipeimport`;

    testScrapeUrl(url: string): Observable<RecipeScrapeTestResult> {
        return this.http.post<RecipeScrapeTestResult>(`${this.apiUrl}/test-scrape-url`, JSON.stringify(url), {
            headers: { 'Content-Type': 'application/json' }
        });
    }

    importFromUrl(url: string): Observable<RecipeCreateResponse> {
        return this.http.post<RecipeCreateResponse>(`${this.apiUrl}/create/url`, JSON.stringify(url), {
            headers: { 'Content-Type': 'application/json' }
        });
    }

    bulkImportFromUrls(urls: string[]): Observable<RecipeCreateResponse[]> {
        return this.http.post<RecipeCreateResponse[]>(`${this.apiUrl}/create/url/bulk`, urls);
    }

    importFromImage(imageData: Blob): Observable<RecipeCreateResponse> {
        const formData = new FormData();
        formData.append('imageData', imageData);
        return this.http.post<RecipeCreateResponse>(`${this.apiUrl}/create/image`, formData);
    }

    importFromHtmlOrJson(htmlOrJson: string): Observable<RecipeCreateResponse> {
        return this.http.post<RecipeCreateResponse>(`${this.apiUrl}/create/html-or-json`, JSON.stringify(htmlOrJson), {
            headers: { 'Content-Type': 'application/json' }
        });
    }

    importFromZip(zipData: Blob): Observable<RecipeCreateResponse[]> {
        const formData = new FormData();
        formData.append('zipData', zipData);
        return this.http.post<RecipeCreateResponse[]>(`${this.apiUrl}/create/zip`, formData);
    }
}
