import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MessageThreadModel } from '../models/i-message-thread.model';
import { MessageModel } from '../models/message.model';
import { SendMessageRequestModel } from '../models/send-message-request.model';

@Injectable({
    providedIn: 'root'
})
export class MessagingService {
    private http = inject(HttpClient);

    private readonly apiUrl = 'api/messaging';



    getMessageThreads(): Observable<MessageThreadModel[]> {
        return this.http.get<MessageThreadModel[]>(`${this.apiUrl}/threads`);
    }

    getMessageThread(threadId: number): Observable<MessageThreadModel> {
        return this.http.get<MessageThreadModel>(`${this.apiUrl}/threads/${threadId}`);
    }

    getMessages(threadId: number): Observable<MessageModel[]> {
        return this.http.get<MessageModel[]>(`${this.apiUrl}/threads/${threadId}/messages`);
    }

    sendMessage(request: SendMessageRequestModel): Observable<{ messageId: number }> {
        return this.http.post<{ messageId: number }>(`${this.apiUrl}/messages`, request);
    }

    markThreadAsRead(threadId: number): Observable<void> {
        return this.http.patch<void>(`${this.apiUrl}/threads/${threadId}/read`, {});
    }

    markMessageAsRead(messageId: number): Observable<void> {
        return this.http.patch<void>(`${this.apiUrl}/messages/${messageId}/read`, {});
    }

    deleteThread(threadId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/threads/${threadId}`);
    }

    archiveThread(threadId: number): Observable<void> {
        return this.http.patch<void>(`${this.apiUrl}/threads/${threadId}/archive`, {});
    }

    pinThread(threadId: number): Observable<void> {
        return this.http.patch<void>(`${this.apiUrl}/threads/${threadId}/pin`, {});
    }

    unpinThread(threadId: number): Observable<void> {
        return this.http.patch<void>(`${this.apiUrl}/threads/${threadId}/unpin`, {});
    }

    createThread(participantIds: number[]): Observable<{ threadId: number }> {
        return this.http.post<{ threadId: number }>(`${this.apiUrl}/threads`, {
            participantIds
        });
    }

    searchThreads(query: string): Observable<MessageThreadModel[]> {
        return this.http.get<MessageThreadModel[]>(`${this.apiUrl}/threads/search`, {
            params: { query }
        });
    }
} 