import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ScrapedRecipeModel, ScrapeUrlRequest, ImportFromUrlRequest, RecipeScrapingResponseModel } from '../models/recipe-scraping.model';

@Injectable({ providedIn: 'root' })
export class RecipeScrapingService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/RecipeScraping`;

  testScrape(request: ScrapeUrlRequest): Observable<ScrapedRecipeModel> {
    return this.http.post<ScrapedRecipeModel>(`${this.apiUrl}/test-scrape-url`, request);
  }

  importFromUrl(request: ImportFromUrlRequest): Observable<RecipeScrapingResponseModel> {
    return this.http.post<RecipeScrapingResponseModel>(`${this.apiUrl}/create/url`, request);
  }
}
