import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { delay, tap } from 'rxjs/operators';
import { RestrictionModel } from '../models/restriction.model'; // Assuming you might fetch restrictions later

@Injectable({
  providedIn: 'root',
})
export class RestrictionService {
  private apiUrl = '/api/Restriction';

  constructor(private http: HttpClient) {}

  /**
   * Fetches a list of curated ingredients for multi-select.
   * Maps to Backend IRestrictionOrchestrationService.GetCuratedIngredientsAsync
   */
  getCuratedIngredients(): Observable<string[]> {
    // --- Actual API Call (Uncomment when ready) ---
    return this.http
      .get<string[]>(`${this.apiUrl}/curated-ingredients`)
      .pipe(tap((data) => console.log('Fetched curated ingredients:', data)));
  }

  /**
   * Fetches a list of micronutrients for multi-select.
   * Maps to Backend IRestrictionOrchestrationService.GetMicronutrientsAsync
   */
  getMicronutrients(): Observable<string[]> {
    // --- Actual API Call (Uncomment when ready) ---
    return this.http
      .get<string[]>(`${this.apiUrl}/micronutrients`)
      .pipe(tap((data) => console.log('Fetched micronutrients:', data)));
  }
}
