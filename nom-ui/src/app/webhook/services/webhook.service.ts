import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { WebhookResponseModel, WebhookCreateRequestModel, WebhookUpdateRequestModel } from '../models/webhook.models';

@Injectable({
    providedIn: 'root'
})
export class WebhookService {
    private http = inject(HttpClient);
    private apiUrl = `${environment.apiUrl}/webhook`;

    getWebhooks(householdId: number): Observable<WebhookResponseModel[]> {
        return this.http.get<WebhookResponseModel[]>(this.apiUrl, { params: { householdId } });
    }

    getWebhook(id: number): Observable<WebhookResponseModel> {
        return this.http.get<WebhookResponseModel>(`${this.apiUrl}/${id}`);
    }

    createWebhook(request: WebhookCreateRequestModel): Observable<number> {
        return this.http.post<number>(this.apiUrl, request);
    }

    updateWebhook(id: number, request: WebhookUpdateRequestModel): Observable<WebhookResponseModel> {
        return this.http.put<WebhookResponseModel>(`${this.apiUrl}/${id}`, request);
    }

    deleteWebhook(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }

    testWebhook(id: number): Observable<{ success: boolean; message: string }> {
        return this.http.post<{ success: boolean; message: string }>(`${this.apiUrl}/${id}/test`, {});
    }
}
