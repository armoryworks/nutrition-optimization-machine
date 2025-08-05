// File: nom-ui/src/app/communication/components/messaging-inbox/messaging-inbox.component.ts

import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, NonNullableFormBuilder, FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { MatBadgeModule } from '@angular/material/badge';
import { Router } from '@angular/router';
import { ViewEncapsulation } from '@angular/core';
import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';
import { MessagingService } from '../../services/messaging.service';
import { MessageThreadModel } from '../../models/i-message-thread.model';
import { MessageParticipantModel } from '../../models/i-message-participant.model';

@Component({
  selector: 'nom-messaging-inbox',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatButtonModule,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    MatChipsModule,
    MatTooltipModule,
    MatProgressSpinnerModule,
    MatDividerModule,
    MatBadgeModule,
    BasePageComponent,
  ],
  templateUrl: './messaging-inbox.component.html',
  styleUrls: ['./messaging-inbox.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class MessagingInboxComponent implements OnInit {
  messageThreads: MessageThreadModel[] = [];
  loading = false;
  error = '';
  searchTerm = '';

  pageConfig: BasePageConfig = {
    title: 'Messages',
    subtitle: 'View and manage your conversations',
    showRefreshButton: true,
    refreshButtonText: 'Refresh',
    maxWidth: '1200px',
  };

  displayedColumns: string[] = ['participant', 'lastMessage', 'unreadCount', 'lastActivity', 'actions'];

  constructor(
    private messagingService: MessagingService,
    private router: Router,
    private fb: NonNullableFormBuilder
  ) { }

  ngOnInit(): void {
    this.loadMessageThreads();
  }

  loadMessageThreads(): void {
    this.loading = true;
    this.error = '';

    this.messagingService.getMessageThreads().subscribe({
      next: (threads) => {
        this.messageThreads = threads;
        this.loading = false;
      },
      error: (error) => {
        this.error = 'Failed to load messages';
        this.loading = false;
        console.error('Error loading message threads:', error);
      },
    });
  }

  onRefresh(): void {
    this.loadMessageThreads();
  }

  onRetry(): void {
    this.loadMessageThreads();
  }

  openThread(thread: MessageThreadModel): void {
    if (thread.id) {
      this.router.navigate(['/communication/thread', thread.id]);
    }
  }

  startNewConversation(): void {
    this.router.navigate(['/communication/new']);
  }

  markAsRead(thread: MessageThreadModel): void {
    if (thread.id) {
      this.messagingService.markThreadAsRead(thread.id).subscribe({
        next: () => {
          // Update the thread in the list
          const index = this.messageThreads.findIndex(t => t.id === thread.id);
          if (index !== -1) {
            this.messageThreads[index].unreadCount = 0;
          }
        },
        error: (error) => {
          console.error('Error marking thread as read:', error);
        },
      });
    }
  }

  deleteThread(thread: MessageThreadModel): void {
    if (thread.id) {
      this.messagingService.deleteThread(thread.id).subscribe({
        next: () => {
          // Remove the thread from the list
          this.messageThreads = this.messageThreads.filter(t => t.id !== thread.id);
        },
        error: (error) => {
          console.error('Error deleting thread:', error);
        },
      });
    }
  }

  getParticipantDisplayName(thread: MessageThreadModel): string {
    if (thread.participants && thread.participants.length > 0) {
      return thread.participants.map(p => p.displayName).join(', ');
    }
    return 'Unknown';
  }

  getLastMessagePreview(thread: MessageThreadModel): string {
    if (thread.lastMessage) {
      return thread.lastMessage.content.length > 50
        ? thread.lastMessage.content.substring(0, 50) + '...'
        : thread.lastMessage.content;
    }
    return 'No messages';
  }

  getLastActivityDate(thread: MessageThreadModel): string {
    if (thread.lastActivity) {
      const date = new Date(thread.lastActivity);
      const now = new Date();
      const diffInHours = (now.getTime() - date.getTime()) / (1000 * 60 * 60);

      if (diffInHours < 1) {
        return 'Just now';
      } else if (diffInHours < 24) {
        return `${Math.floor(diffInHours)}h ago`;
      } else if (diffInHours < 168) { // 7 days
        return `${Math.floor(diffInHours / 24)}d ago`;
      } else {
        return date.toLocaleDateString();
      }
    }
    return 'Never';
  }

  hasUnreadMessages(thread: MessageThreadModel): boolean {
    return !!(thread.unreadCount && thread.unreadCount > 0);
  }
}