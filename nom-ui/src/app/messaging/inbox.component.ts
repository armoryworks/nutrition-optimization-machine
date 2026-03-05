import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { MessagingService, MessageThread } from '../core/services/messaging.service';
import { LoadingService } from '../core/services/loading.service';

@Component({
  selector: 'nom-inbox',
  imports: [
    RouterLink,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatMenuModule,
  ],
  template: `
    <div class="nom-dashboard">
      <div class="nom-dashboard__header">
        <div class="nom-dashboard__header-left">
          <h1 class="nom-dashboard__title">Messages</h1>
          @if (unreadCount() > 0) {
            <span class="nom-dashboard__subtitle">{{ unreadCount() }} unread</span>
          }
        </div>
        <div class="nom-dashboard__header-right">
          <a mat-flat-button routerLink="/messages/new">
            <mat-icon>edit</mat-icon>
            Compose
          </a>
        </div>
      </div>

      @if (loading()) {
        <div class="nom-inbox__loading">
          <mat-spinner diameter="40"></mat-spinner>
        </div>
      } @else if (threads().length === 0) {
        <div class="nom-dashboard__empty">
          <mat-icon class="nom-dashboard__empty-icon">forum</mat-icon>
          <h2 class="nom-dashboard__empty-title">No messages</h2>
          <p class="nom-dashboard__empty-message">Start a conversation with household members.</p>
          <a mat-flat-button routerLink="/messages/new" class="nom-dashboard__empty-action">
            <mat-icon>edit</mat-icon>
            Compose Message
          </a>
        </div>
      } @else {
        <div class="nom-inbox__list">
          @for (thread of sortedThreads(); track thread.id) {
            <div class="nom-inbox__thread"
                 [class.nom-inbox__thread--unread]="thread.unreadCount > 0"
                 [class.nom-inbox__thread--pinned]="thread.isPinned"
                 (click)="openThread(thread.id)">
              @if (thread.isPinned) {
                <mat-icon class="nom-inbox__pin-icon">push_pin</mat-icon>
              }
              <div class="nom-inbox__thread-content">
                <div class="nom-inbox__thread-top">
                  <span class="nom-inbox__thread-participants">
                    {{ thread.participantNames.join(', ') }}
                  </span>
                  <span class="nom-inbox__thread-date">{{ formatDate(thread.lastMessageDate) }}</span>
                </div>
                <span class="nom-inbox__thread-subject">{{ thread.subject }}</span>
                <span class="nom-inbox__thread-preview">{{ thread.lastMessageContent }}</span>
              </div>
              @if (thread.unreadCount > 0) {
                <span class="nom-inbox__unread-badge">{{ thread.unreadCount }}</span>
              }
              <button mat-icon-button [matMenuTriggerFor]="threadMenu" (click)="$event.stopPropagation()" aria-label="Thread options">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #threadMenu="matMenu">
                @if (thread.isPinned) {
                  <button mat-menu-item (click)="unpin(thread)">
                    <mat-icon>push_pin</mat-icon>
                    <span>Unpin</span>
                  </button>
                } @else {
                  <button mat-menu-item (click)="pin(thread)">
                    <mat-icon>push_pin</mat-icon>
                    <span>Pin</span>
                  </button>
                }
                <button mat-menu-item (click)="archive(thread)">
                  <mat-icon>archive</mat-icon>
                  <span>Archive</span>
                </button>
                <button mat-menu-item (click)="deleteThread(thread)">
                  <mat-icon>delete</mat-icon>
                  <span>Delete</span>
                </button>
              </mat-menu>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    @use 'variables' as vars;

    .nom-inbox__loading {
      display: flex;
      justify-content: center;
      padding: vars.$spacing-12;
    }

    .nom-inbox__list {
      display: flex;
      flex-direction: column;
    }

    .nom-inbox__thread {
      display: flex;
      align-items: center;
      gap: vars.$spacing-3;
      padding: vars.$spacing-3 vars.$spacing-4;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
      cursor: pointer;
      transition: background vars.$transition-duration-fast vars.$transition-timing;

      &:hover {
        background: var(--mat-sys-surface-container);
      }

      &--unread {
        background: var(--mat-sys-surface-container-low);

        .nom-inbox__thread-subject {
          font-weight: vars.$font-weight-semibold;
        }

        .nom-inbox__thread-participants {
          font-weight: vars.$font-weight-semibold;
        }
      }

      &--pinned {
        border-left: 3px solid var(--mat-sys-primary);
      }
    }

    .nom-inbox__pin-icon {
      font-size: 16px;
      width: 16px;
      height: 16px;
      color: var(--mat-sys-primary);
      flex-shrink: 0;
    }

    .nom-inbox__thread-content {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .nom-inbox__thread-top {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
    }

    .nom-inbox__thread-participants {
      font-size: vars.$font-size-sm;
      color: var(--mat-sys-on-surface);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .nom-inbox__thread-date {
      font-size: vars.$font-size-xs;
      color: var(--mat-sys-on-surface-variant);
      white-space: nowrap;
      flex-shrink: 0;
    }

    .nom-inbox__thread-subject {
      font-size: vars.$font-size-sm;
      color: var(--mat-sys-on-surface);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .nom-inbox__thread-preview {
      font-size: vars.$font-size-sm;
      color: var(--mat-sys-on-surface-variant);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .nom-inbox__unread-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-width: 20px;
      height: 20px;
      border-radius: 10px;
      background: var(--mat-sys-primary);
      color: var(--mat-sys-on-primary);
      font-size: vars.$font-size-xs;
      font-weight: vars.$font-weight-semibold;
      flex-shrink: 0;
      padding: 0 vars.$spacing-1;
    }
  `],
})
export class Inbox implements OnInit {
  private router = inject(Router);
  private messagingService = inject(MessagingService);
  private loadingService = inject(LoadingService);

  threads = signal<MessageThread[]>([]);
  loading = signal(true);

  unreadCount = computed(() => this.threads().reduce((sum, t) => sum + t.unreadCount, 0));

  sortedThreads = computed(() => {
    const list = [...this.threads()].filter(t => !t.isArchived);
    list.sort((a, b) => {
      if (a.isPinned !== b.isPinned) return a.isPinned ? -1 : 1;
      return new Date(b.lastMessageDate).getTime() - new Date(a.lastMessageDate).getTime();
    });
    return list;
  });

  ngOnInit(): void {
    this.loadThreads();
  }

  private loadThreads(): void {
    this.loading.set(true);
    this.messagingService.getThreads().pipe(
      this.loadingService.loading('Loading messages...')
    ).subscribe({
      next: (threads) => {
        this.threads.set(threads);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openThread(id: number): void {
    this.router.navigate(['/messages', id]);
  }

  formatDate(dateStr: string): string {
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffDays = Math.floor(diffMs / 86400000);
    if (diffDays === 0) return date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
    if (diffDays === 1) return 'Yesterday';
    if (diffDays < 7) return date.toLocaleDateString(undefined, { weekday: 'short' });
    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  }

  pin(thread: MessageThread): void {
    this.messagingService.pinThread(thread.id).subscribe(() => {
      this.threads.update(list => list.map(t => t.id === thread.id ? { ...t, isPinned: true } : t));
    });
  }

  unpin(thread: MessageThread): void {
    this.messagingService.unpinThread(thread.id).subscribe(() => {
      this.threads.update(list => list.map(t => t.id === thread.id ? { ...t, isPinned: false } : t));
    });
  }

  archive(thread: MessageThread): void {
    this.messagingService.archiveThread(thread.id).subscribe(() => {
      this.threads.update(list => list.map(t => t.id === thread.id ? { ...t, isArchived: true } : t));
    });
  }

  deleteThread(thread: MessageThread): void {
    this.messagingService.deleteThread(thread.id).subscribe(() => {
      this.threads.update(list => list.filter(t => t.id !== thread.id));
    });
  }
}
