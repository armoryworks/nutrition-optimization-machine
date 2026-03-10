import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ScrapedRecipeModel } from '../models/scraped-recipe.model';
import { ScrapeUrlRequest } from '../models/scrape-url-request.model';
import { ImportFromUrlRequest } from '../models/import-from-url-request.model';
import { RecipeScrapingResponseModel } from '../models/recipe-scraping-response.model';

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
