import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  GroceryConnectStart,
  GroceryExportOptions,
  GroceryExportResult,
  GroceryProviderInfo,
  GroceryStore,
  GroceryStoreSelection,
} from '../models/grocery-export.model';

/**
 * Sends shopping lists to external destinations through the operator's grocery
 * service. Every call degrades gracefully when nothing is configured —
 * `getProviders()` simply comes back empty and the UI hides the feature.
 *
 * `GET /GroceryExport/callback/{provider}` has no method here on purpose: the
 * retailer redirects the browser straight to it and the API bounces back into
 * the app with `?connected=ok|failed&provider=…`, which `ShoppingComponent`
 * reads off the route.
 */
@Injectable({ providedIn: 'root' })
export class GroceryExportService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/GroceryExport`;

  /** Destinations available to this user, with their connection state. Empty = feature off. */
  getProviders(): Observable<GroceryProviderInfo[]> {
    return this.http.get<GroceryProviderInfo[]>(`${this.apiUrl}/providers`);
  }

  /**
   * Send a list to a destination. Answers 200 even when the export logically
   * fails — callers must branch on `result.success` and surface `result.error`.
   */
  exportList(shoppingListId: number, options: GroceryExportOptions): Observable<GroceryExportResult> {
    return this.http.post<GroceryExportResult>(`${this.apiUrl}/list/${shoppingListId}`, options);
  }

  /**
   * Begin linking a retailer account. The returned URL must be visited as a
   * full-page navigation — retailer consent screens refuse to frame.
   */
  startConnection(provider: string, returnUrl: string): Observable<GroceryConnectStart> {
    const params = new HttpParams().set('returnUrl', returnUrl);
    return this.http.post<GroceryConnectStart>(
      `${this.apiUrl}/connect/${encodeURIComponent(provider)}`,
      {},
      { params },
    );
  }

  /** Remove this user's link to a retailer. */
  disconnect(provider: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/connect/${encodeURIComponent(provider)}`);
  }

  /** Stores near a postal code, for choosing which cart to fill. */
  findStores(provider: string, postalCode: string): Observable<GroceryStore[]> {
    const params = new HttpParams().set('postalCode', postalCode);
    return this.http.get<GroceryStore[]>(
      `${this.apiUrl}/stores/${encodeURIComponent(provider)}`,
      { params },
    );
  }

  /** Pin the store this user's carts are built at. */
  setStore(provider: string, selection: GroceryStoreSelection): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/stores/${encodeURIComponent(provider)}`, selection);
  }
}
