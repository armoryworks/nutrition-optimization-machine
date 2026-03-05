import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface WebhookResponse {
  id: number;
  householdId: number;
  name: string;
  url: string;
  eventType: string;
  isActive: boolean;
}

export interface WebhookCreateRequest {
  householdId: number;
  name: string;
  url: string;
  eventType: string;
  isActive: boolean;
}

export interface WebhookUpdateRequest {
  name: string;
  url: string;
  eventType: string;
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class WebhookService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Webhook`;

  getWebhooks(householdId: number): Observable<WebhookResponse[]> {
    return this.http.get<WebhookResponse[]>(this.apiUrl, { params: { householdId } });
  }

  getWebhook(id: number): Observable<WebhookResponse> {
    return this.http.get<WebhookResponse>(`${this.apiUrl}/${id}`);
  }

  createWebhook(request: WebhookCreateRequest): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(this.apiUrl, request);
  }

  updateWebhook(id: number, request: WebhookUpdateRequest): Observable<WebhookResponse> {
    return this.http.put<WebhookResponse>(`${this.apiUrl}/${id}`, request);
  }

  deleteWebhook(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  testWebhook(id: number): Observable<{ success: boolean }> {
    return this.http.post<{ success: boolean }>(`${this.apiUrl}/${id}/test`, {});
  }
}
