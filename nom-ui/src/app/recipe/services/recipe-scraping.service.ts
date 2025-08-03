import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface RecipeScrapingRequest {
    url: string;
    importKeywordsAsTags?: boolean;
    stayInEditMode?: boolean;
}

export interface RecipeScrapingDataRequest {
    data: string;
    importKeywordsAsTags?: boolean;
    stayInEditMode?: boolean;
}

export interface RecipeScrapingTestRequest {
    url: string;
    useOpenAI?: boolean;
}

export interface RecipeBulkScrapingItem {
    url: string;
    tags?: string[];
    categories?: string[];
}

export interface RecipeBulkScrapingRequest {
    imports: RecipeBulkScrapingItem[];
}

export interface RecipeScrapingResponse {
    recipeId: number;
    recipeName: string;
    message: string;
    success: boolean;
    error?: string;
}

export interface RecipeBulkScrapingResponse {
    reportId: number;
    results: RecipeScrapingResponse[];
    totalProcessed: number;
    successCount: number;
    errorCount: number;
}

export interface ScrapedRecipe {
    name: string;
    description?: string;
    image?: string;
    sourceUrl?: string;
    sourceSite?: string;
    prepTime?: string;
    cookTime?: string;
    totalTime?: string;
    recipeYield?: string;
    recipeYieldQuantity?: number;
    recipeServings?: number;
    ingredients: ScrapedIngredient[];
    steps: ScrapedStep[];
    tags: string[];
    categories: string[];
}

export interface ScrapedIngredient {
    name: string;
    quantity?: number;
    unit?: string;
    notes?: string;
}

export interface ScrapedStep {
    order: number;
    instruction: string;
    image?: string;
}

@Injectable({
    providedIn: 'root'
})
export class RecipeScrapingService {
    private readonly apiUrl = `${environment.apiUrl}/RecipeScraping`;

    constructor(private http: HttpClient) { }

    /**
     * Test recipe scraping from a URL
     */
    testScrapeRecipe(request: RecipeScrapingTestRequest): Observable<ScrapedRecipe> {
        return this.http.post<ScrapedRecipe>(`${this.apiUrl}/test-scrape-url`, request);
    }

    /**
     * Scrape recipe from HTML or JSON data
     */
    scrapeRecipeFromData(request: RecipeScrapingDataRequest): Observable<RecipeScrapingResponse> {
        return this.http.post<RecipeScrapingResponse>(`${this.apiUrl}/create/html-or-json`, request);
    }

    /**
     * Scrape recipe from URL
     */
    scrapeRecipeFromUrl(request: RecipeScrapingRequest): Observable<RecipeScrapingResponse> {
        return this.http.post<RecipeScrapingResponse>(`${this.apiUrl}/create/url`, request);
    }

    /**
     * Bulk scrape recipes from multiple URLs
     */
    bulkScrapeRecipes(request: RecipeBulkScrapingRequest): Observable<RecipeBulkScrapingResponse> {
        return this.http.post<RecipeBulkScrapingResponse>(`${this.apiUrl}/bulk-scrape`, request);
    }

    /**
     * Get scraping report by ID
     */
    getScrapingReport(reportId: number): Observable<RecipeBulkScrapingResponse> {
        return this.http.get<RecipeBulkScrapingResponse>(`${this.apiUrl}/reports/${reportId}`);
    }

    /**
     * Get all scraping reports for the current user
     */
    getScrapingReports(): Observable<RecipeBulkScrapingResponse[]> {
        return this.http.get<RecipeBulkScrapingResponse[]>(`${this.apiUrl}/reports`);
    }
} 