import { Component, inject, signal, effect, ElementRef, viewChild, AfterViewChecked, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MessagingService } from '../core/services/messaging.service';
import { Message } from '../core/models/message.model';
import { MessageThread } from '../core/models/message-thread.model';
import { LoadingService } from '../core/services/loading.service';

@Component({
  selector: 'nom-thread',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './thread.component.html',
  styleUrl: './thread.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Thread implements AfterViewChecked {
  private route = inject(ActivatedRoute);
  private messagingService = inject(MessagingService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  messagesContainer = viewChild<ElementRef>('messagesContainer');

  thread = signal<MessageThread | null>(null);
  messages = signal<Message[]>([]);
  loading = signal(true);
  sending = signal(false);
  currentPersonId = signal(0);
  newMessage = new FormControl('');
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
    this.messagingService.getThread(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (thread) => {
        this.thread.set(thread);
        this.messagingService.markAsRead(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
        this.loadMessages(id);
      },
      error: () => this.loading.set(false),
    });
  }

  private loadMessages(threadId: number): void {
    this.messagingService.getMessages(threadId).pipe(
      this.loadingService.loading('Loading messages...'),
      takeUntilDestroyed(this.destroyRef),
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
    const content = (this.newMessage.value ?? '').trim();
    const threadId = this.thread()?.id;
    if (!content || !threadId || this.sending()) return;

    this.sending.set(true);
    this.messagingService.sendMessage({ threadId, content }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (msg) => {
        this.messages.update(list => [...list, msg]);
        this.newMessage.setValue('');
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
