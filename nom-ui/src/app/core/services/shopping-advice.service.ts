import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BasketItem, ShopRecommendation } from '../models/shop-recommendation.model';

@Injectable({ providedIn: 'root' })
export class ShoppingAdviceService {
  private http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/ShoppingAdvice`;

  whereToShop(householdId: number, postalCode: string, basket: BasketItem[]): Observable<ShopRecommendation> {
    return this.http.post<ShopRecommendation>(`${this.url}/where-to-shop`, {
      householdId,
      postalCode,
      basket,
    });
  }
}
