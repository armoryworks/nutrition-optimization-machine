import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { KitchenSettings, KitchenSettingsUpdate, RecipeToolCheck } from '../models/kitchen-tools.model';

@Injectable({ providedIn: 'root' })
export class KitchenToolsService {
  private http = inject(HttpClient);
  private readonly householdUrl = `${environment.apiUrl}/Household`;

  getSettings(householdId: number): Observable<KitchenSettings> {
    return this.http.get<KitchenSettings>(`${this.householdUrl}/${householdId}/kitchen-tools`);
  }

  saveSettings(householdId: number, update: KitchenSettingsUpdate): Observable<KitchenSettings> {
    return this.http.put<KitchenSettings>(`${this.householdUrl}/${householdId}/kitchen-tools`, update);
  }

  checkRecipe(householdId: number, recipeId: number): Observable<RecipeToolCheck> {
    return this.http.get<RecipeToolCheck>(`${this.householdUrl}/${householdId}/kitchen-tools/recipes/${recipeId}`);
  }
}
