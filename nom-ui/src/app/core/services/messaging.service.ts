import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface MessageThread {
  id: number;
  subject: string;
  lastMessageContent: string;
  lastMessageDate: string;
  lastMessageSenderName: string;
  participantNames: string[];
  unreadCount: number;
  isPinned: boolean;
  isArchived: boolean;
  threadType: number;
  recipeId: number | null;
  ingredientId: number | null;
  planId: number | null;
}

export interface Message {
  id: number;
  threadId: number;
  senderPersonId: number;
  senderName: string;
  content: string;
  timestamp: string;
  isRead: boolean;
}

export interface CreateThreadRequest {
  participantPersonIds: number[];
  subject: string;
  initialMessage: string;
  threadType?: number;
  recipeId?: number | null;
  ingredientId?: number | null;
  planId?: number | null;
}

export interface SendMessageRequest {
  threadId: number;
  content: string;
}

@Injectable({ providedIn: 'root' })
export class MessagingService {
  private http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Messaging`;

  getThreads(): Observable<MessageThread[]> {
    return this.http.get<MessageThread[]>(`${this.apiUrl}/threads`);
  }

  getThread(id: number): Observable<MessageThread> {
    return this.http.get<MessageThread>(`${this.apiUrl}/threads/${id}`);
  }

  getMessages(threadId: number): Observable<Message[]> {
    return this.http.get<Message[]>(`${this.apiUrl}/threads/${threadId}/messages`);
  }

  createThread(request: CreateThreadRequest): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.apiUrl}/threads`, request);
  }

  sendMessage(request: SendMessageRequest): Observable<Message> {
    return this.http.post<Message>(`${this.apiUrl}/messages`, request);
  }

  markAsRead(threadId: number): Observable<void> {
    return this.http.patch<void>(`${this.apiUrl}/threads/${threadId}/read`, {});
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

  searchThreads(query: string): Observable<MessageThread[]> {
    return this.http.get<MessageThread[]>(`${this.apiUrl}/threads/search`, { params: { query } });
  }
}
