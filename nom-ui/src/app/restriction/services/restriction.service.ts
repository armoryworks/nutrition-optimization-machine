import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap } from 'rxjs/operators';

@Injectable({
  providedIn: 'root',
})
export class RestrictionService {
  private http = inject(HttpClient);

  private apiUrl = '/api/Restriction';



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
