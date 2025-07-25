// File: nom-ui/src/app/communication/services/communication.service.ts

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SendMessageRequestModel } from '../models/send-message-request.model';


@Injectable({
  providedIn: 'root'
})
export class CommunicationService {
  private readonly apiUrl = `api/messaging`;

  constructor(private http: HttpClient) { }

  sendMessage(request: SendMessageRequestModel): Observable<{ messageId: number }> {
    return this.http.post<{ messageId: number }>(`${this.apiUrl}/messages`, request);
  }
}