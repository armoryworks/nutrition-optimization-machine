// File: nom-ui/src/app/communication/components/messaging-inbox/messaging-inbox.component.ts

import { Component, OnInit, inject, signal } from '@angular/core';

import { ReactiveFormsModule, NonNullableFormBuilder, FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { MatChipsModule } from '@angular/material/chips';
import { MatBadgeModule } from '@angular/material/badge';
import { Router } from '@angular/router';
import { ViewEncapsulation } from '@angular/core';
import { AmwButtonComponent, AmwCardComponent, AmwInputComponent, AmwIconButtonComponent, AmwIconComponent, AmwProgressSpinnerComponent } from 'angular-material-wrap';

import { BasePageComponent, BasePageConfig } from '../../../common/components/base-page/base-page.component';
import { MessagingService } from '../../services/messaging.service';
import { MessageThreadModel } from '../../models/i-message-thread.model';


@Component({
  selector: 'nom-messaging-inbox',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    FormsModule,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    MatChipsModule,
    MatBadgeModule,
    AmwButtonComponent,
    AmwCardComponent,
    AmwInputComponent,
    AmwIconButtonComponent,
    AmwIconComponent,
    AmwProgressSpinnerComponent,
    BasePageComponent,
  ],
  templateUrl: './messaging-inbox.component.html',
  styleUrls: ['./messaging-inbox.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class MessagingInboxComponent implements OnInit {
  private messagingService = inject(MessagingService);
  private router = inject(Router);
  private fb = inject(NonNullableFormBuilder);

  messageThreads = signal<MessageThreadModel[]>([]);
  loading = signal(false);
  error = signal('');
  searchTerm = signal('');

  pageConfig: BasePageConfig = {
    title: 'Messages',
    subtitle: 'View and manage your conversations',
    showRefreshButton: true,
    refreshButtonText: 'Refresh',
    maxWidth: '1200px',
  };

  displayedColumns: string[] = ['participant', 'lastMessage', 'unreadCount', 'lastActivity', 'actions'];

  ngOnInit(): void {
    this.loadMessageThreads();
  }

  loadMessageThreads(): void {
    this.loading.set(true);
    this.error.set('');

    this.messagingService.getMessageThreads().subscribe({
      next: (threads) => {
        this.messageThreads.set(threads);
        this.loading.set(false);
      },
      error: (error) => {
        this.error.set('Failed to load messages');
        this.loading.set(false);
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
      this.router.navigate(['/messaging/thread', thread.id]);
    }
  }

  startNewConversation(): void {
    this.router.navigate(['/messaging/new']);
  }

  markAsRead(thread: MessageThreadModel): void {
    if (thread.id) {
      this.messagingService.markThreadAsRead(thread.id).subscribe({
        next: () => {
          // Update the thread in the list
          const threads = this.messageThreads();
          const index = threads.findIndex(t => t.id === thread.id);
          if (index !== -1) {
            threads[index].unreadCount = 0;
            this.messageThreads.set([...threads]);
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
          this.messageThreads.set(this.messageThreads().filter(t => t.id !== thread.id));
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