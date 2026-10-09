import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  KitchenSettings,
  KitchenSettingsUpdate,
  KitchenToolSuggestion,
  KitchenToolSuggestionStatus,
  KitchenToolTaggingBatchResult,
  KitchenToolTaggingStatus,
  RecipeToolCheck,
} from '../models/kitchen-tools.model';

@Injectable({ providedIn: 'root' })
export class KitchenToolsService {
  private http = inject(HttpClient);
  private readonly householdUrl = `${environment.apiUrl}/Household`;
  private readonly adminUrl = `${environment.apiUrl}/KitchenTools`;

  getSettings(householdId: number): Observable<KitchenSettings> {
    return this.http.get<KitchenSettings>(`${this.householdUrl}/${householdId}/kitchen-tools`);
  }

  saveSettings(householdId: number, update: KitchenSettingsUpdate): Observable<KitchenSettings> {
    return this.http.put<KitchenSettings>(`${this.householdUrl}/${householdId}/kitchen-tools`, update);
  }

  checkRecipe(householdId: number, recipeId: number): Observable<RecipeToolCheck> {
    return this.http.get<RecipeToolCheck>(`${this.householdUrl}/${householdId}/kitchen-tools/recipes/${recipeId}`);
  }

  getTaggingStatus(): Observable<KitchenToolTaggingStatus> {
    return this.http.get<KitchenToolTaggingStatus>(`${this.adminUrl}/status`);
  }

  getSuggestions(status: KitchenToolSuggestionStatus): Observable<KitchenToolSuggestion[]> {
    return this.http.get<KitchenToolSuggestion[]>(`${this.adminUrl}/suggestions`, { params: { status } });
  }

  approveSuggestion(id: number, displayName: string, category: string): Observable<{ toolId: number; recipesLinked: number }> {
    return this.http.post<{ toolId: number; recipesLinked: number }>(`${this.adminUrl}/suggestions/${id}/approve`, { displayName, category });
  }

  rejectSuggestion(id: number): Observable<void> {
    return this.http.post<void>(`${this.adminUrl}/suggestions/${id}/reject`, {});
  }

  tagBatch(size = 6): Observable<KitchenToolTaggingBatchResult> {
    return this.http.post<KitchenToolTaggingBatchResult>(`${this.adminUrl}/tag-batch`, {}, { params: { size } });
  }
}
