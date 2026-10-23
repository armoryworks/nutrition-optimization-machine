import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  BarcodeResult,
  HouseholdShoppingItem,
  HouseholdShoppingItemAdded,
  HouseholdShoppingItemCreate,
  HouseholdShoppingItemUpdate,
  IngredientMeasurements,
  IngredientVerification,
  IngredientVerificationConfirm,
  LabelScanResult,
  NeedsVerification,
  NotFoodResult,
  ShoppingCheckRequest,
  ShoppingCheckResult,
  ShoppingState,
} from '../models/household-shopping.model';

@Injectable({ providedIn: 'root' })
export class HouseholdShoppingService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Shopping`;

  getState(householdId: number): Observable<ShoppingState> {
    const params = new HttpParams().set('householdId', householdId);
    return this.http.get<ShoppingState>(this.apiUrl, { params });
  }

  addItem(body: HouseholdShoppingItemCreate): Observable<HouseholdShoppingItemAdded> {
    return this.http
      .post<HouseholdShoppingItem>(`${this.apiUrl}/items`, body, { observe: 'response' })
      .pipe(map((response) => ({ item: response.body as HouseholdShoppingItem, created: response.status === 201 })));
  }

  updateItem(id: number, body: HouseholdShoppingItemUpdate): Observable<HouseholdShoppingItem> {
    return this.http.put<HouseholdShoppingItem>(`${this.apiUrl}/items/${id}`, body);
  }

  removeItem(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/items/${id}`);
  }

  setChecked(body: ShoppingCheckRequest): Observable<ShoppingCheckResult> {
    return this.http.post<ShoppingCheckResult>(`${this.apiUrl}/checks`, body);
  }

  clearChecks(householdId: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/checks/clear`, { householdId });
  }

  needsVerification(householdId: number, ingredientIds: number[]): Observable<NeedsVerification[]> {
    const params = new HttpParams()
      .set('householdId', householdId)
      .set('ingredientIds', ingredientIds.join(','));
    return this.http.get<NeedsVerification[]>(`${this.apiUrl}/needs-verification`, { params });
  }

  skipVerification(householdId: number, ingredientId: number): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/verification/${ingredientId}/skip`, { householdId });
  }

  markNotFood(householdId: number, ingredientId: number): Observable<NotFoodResult> {
    return this.http.post<NotFoodResult>(`${this.apiUrl}/verification/${ingredientId}/not-food`, { householdId });
  }

  uploadLabel(householdId: number, ingredientId: number, image: File): Observable<LabelScanResult> {
    return this.http.post<LabelScanResult>(
      `${this.apiUrl}/verification/${ingredientId}/label`,
      this.imageForm(householdId, image),
    );
  }

  confirmLabel(verificationId: number, body: IngredientVerificationConfirm): Observable<IngredientVerification> {
    return this.http.put<IngredientVerification>(`${this.apiUrl}/verification/${verificationId}`, body);
  }

  submitBarcode(householdId: number, ingredientId: number, upc: string): Observable<BarcodeResult> {
    return this.http.post<BarcodeResult>(`${this.apiUrl}/verification/${ingredientId}/barcode`, {
      householdId,
      upc,
    });
  }

  decodeBarcodeImage(householdId: number, ingredientId: number, image: File): Observable<BarcodeResult> {
    return this.http.post<BarcodeResult>(
      `${this.apiUrl}/verification/${ingredientId}/barcode/image`,
      this.imageForm(householdId, image),
    );
  }

  saveMeasurements(ingredientId: number, body: IngredientMeasurements): Observable<IngredientVerification> {
    return this.http.post<IngredientVerification>(
      `${this.apiUrl}/verification/${ingredientId}/measurements`,
      body,
    );
  }

  private imageForm(householdId: number, image: File): FormData {
    const form = new FormData();
    form.append('householdId', String(householdId));
    form.append('image', image, image.name);
    return form;
  }
}
