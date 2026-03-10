import { Component, inject, signal, effect, ElementRef, viewChild, AfterViewChecked } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MessagingService, Message, MessageThread } from '../core/services/messaging.service';
import { LoadingService } from '../core/services/loading.service';

@Component({
  selector: 'nom-thread',
  imports: [
    RouterLink,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  template: `
    <div class="nom-thread">
      <div class="nom-thread__header">
        <a mat-icon-button routerLink="/messages" aria-label="Back to inbox">
          <mat-icon>arrow_back</mat-icon>
        </a>
        <div class="nom-thread__header-info">
          <h1 class="nom-thread__subject">{{ thread()?.subject ?? 'Conversation' }}</h1>
          <span class="nom-thread__participants">{{ thread()?.participantNames?.join(', ') }}</span>
        </div>
      </div>

      @if (loading()) {
        <div class="nom-thread__loading">
          <mat-spinner diameter="40"></mat-spinner>
        </div>
      } @else {
        <div class="nom-thread__messages" #messagesContainer>
          @for (msg of messages(); track msg.id) {
            <div class="nom-thread__message" [class.nom-thread__message--own]="msg.senderPersonId === currentPersonId()">
              <div class="nom-thread__message-header">
                <span class="nom-thread__message-sender">{{ msg.senderName }}</span>
                <span class="nom-thread__message-time">{{ formatTime(msg.timestamp) }}</span>
              </div>
              <div class="nom-thread__message-body">{{ msg.content }}</div>
            </div>
          }
        </div>

        <div class="nom-thread__compose">
          <mat-form-field appearance="outline" class="nom-thread__input">
            <input matInput
                   [(ngModel)]="newMessage"
                   placeholder="Type a message..."
                   (keydown.enter)="send()"
                   [disabled]="sending()" />
          </mat-form-field>
          <button mat-flat-button (click)="send()" [disabled]="!newMessage.trim() || sending()">
            @if (sending()) {
              <mat-spinner diameter="20"></mat-spinner>
            } @else {
              <mat-icon>send</mat-icon>
            }
          </button>
        </div>
      }
    </div>
  `,
  styles: [`
    @use 'variables' as vars;

    .nom-thread {
      display: flex;
      flex-direction: column;
      height: calc(100vh - vars.$header-height - vars.$footer-height - vars.$spacing-8);
      max-width: vars.$content-max-narrow;
      margin: 0 auto;
      padding: 0 vars.$spacing-4;
    }

    .nom-thread__header {
      display: flex;
      align-items: center;
      gap: vars.$spacing-3;
      padding: vars.$spacing-3 0;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }

    .nom-thread__header-info {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .nom-thread__subject {
      font-size: vars.$font-size-lg;
      font-weight: vars.$font-weight-semibold;
      margin: 0;
      color: var(--mat-sys-on-surface);
    }

    .nom-thread__participants {
      font-size: vars.$font-size-sm;
      color: var(--mat-sys-on-surface-variant);
    }

    .nom-thread__loading {
      display: flex;
      justify-content: center;
      align-items: center;
      flex: 1;
    }

    .nom-thread__messages {
      flex: 1;
      overflow-y: auto;
      padding: vars.$spacing-4 0;
      display: flex;
      flex-direction: column;
      gap: vars.$spacing-3;
    }

    .nom-thread__message {
      max-width: 75%;
      align-self: flex-start;
    }

    .nom-thread__message--own {
      align-self: flex-end;

      .nom-thread__message-body {
        background: var(--mat-sys-primary-container);
        color: var(--mat-sys-on-primary-container);
      }
    }

    .nom-thread__message-header {
      display: flex;
      align-items: baseline;
      gap: vars.$spacing-2;
      margin-bottom: 2px;
      padding: 0 vars.$spacing-2;
    }

    .nom-thread__message-sender {
      font-size: vars.$font-size-xs;
      font-weight: vars.$font-weight-semibold;
      color: var(--mat-sys-on-surface-variant);
    }

    .nom-thread__message-time {
      font-size: vars.$font-size-xs;
      color: var(--mat-sys-on-surface-variant);
      opacity: 0.7;
    }

    .nom-thread__message-body {
      padding: vars.$spacing-2 vars.$spacing-3;
      background: var(--mat-sys-surface-container);
      border-radius: vars.$nom-border-radius-lg;
      font-size: vars.$font-size-sm;
      line-height: vars.$line-height-normal;
      color: var(--mat-sys-on-surface);
      white-space: pre-wrap;
      word-break: break-word;
    }

    .nom-thread__compose {
      display: flex;
      align-items: center;
      gap: vars.$spacing-2;
      padding: vars.$spacing-3 0;
      border-top: 1px solid var(--mat-sys-outline-variant);
    }

    .nom-thread__input {
      flex: 1;
    }
  `],
})
export class Thread implements AfterViewChecked {
  private route = inject(ActivatedRoute);
  private messagingService = inject(MessagingService);
  private loadingService = inject(LoadingService);

  messagesContainer = viewChild<ElementRef>('messagesContainer');

  thread = signal<MessageThread | null>(null);
  messages = signal<Message[]>([]);
  loading = signal(true);
  sending = signal(false);
  currentPersonId = signal(0);
  newMessage = '';
  private shouldScroll = false;

  private params = toSignal(this.route.params);

  constructor() {
    // Get current person ID from stored user data
    try {
      const userData = localStorage.getItem('nom_user');
      if (userData) {
        const parsed = JSON.parse(userData);
        this.currentPersonId.set(parsed.personId ?? 0);
      }
    } catch { /* ignore */ }

    effect(() => {
      const id = Number(this.params()?.['id']);
      if (id) this.loadThread(id);
    });
  }

  ngAfterViewChecked(): void {
    if (this.shouldScroll) {
      this.scrollToBottom();
      this.shouldScroll = false;
    }
  }

  private loadThread(id: number): void {
    this.loading.set(true);
    this.messagingService.getThread(id).subscribe({
      next: (thread) => {
        this.thread.set(thread);
        this.messagingService.markAsRead(id).subscribe();
        this.loadMessages(id);
      },
      error: () => this.loading.set(false),
    });
  }

  private loadMessages(threadId: number): void {
    this.messagingService.getMessages(threadId).pipe(
      this.loadingService.loading('Loading messages...')
    ).subscribe({
      next: (messages) => {
        this.messages.set(messages);
        this.loading.set(false);
        this.shouldScroll = true;
      },
      error: () => this.loading.set(false),
    });
  }

  send(): void {
    const content = this.newMessage.trim();
    const threadId = this.thread()?.id;
    if (!content || !threadId || this.sending()) return;

    this.sending.set(true);
    this.messagingService.sendMessage({ threadId, content }).subscribe({
      next: (msg) => {
        this.messages.update(list => [...list, msg]);
        this.newMessage = '';
        this.sending.set(false);
        this.shouldScroll = true;
      },
      error: () => this.sending.set(false),
    });
  }

  formatTime(dateStr: string): string {
    const date = new Date(dateStr);
    const now = new Date();
    const isToday = date.toDateString() === now.toDateString();
    if (isToday) return date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
  }

  private scrollToBottom(): void {
    const el = this.messagesContainer()?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }
}
