import { Component, inject, signal, computed, OnInit, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { MessagingService } from '../core/services/messaging.service';
import { MessageThread } from '../core/models/message-thread.model';
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
  templateUrl: './inbox.component.html',
  styleUrl: './inbox.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Inbox implements OnInit {
  private router = inject(Router);
  private messagingService = inject(MessagingService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

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
      this.loadingService.loading('Loading messages...'),
      takeUntilDestroyed(this.destroyRef),
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
    this.messagingService.pinThread(thread.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.threads.update(list => list.map(t => t.id === thread.id ? { ...t, isPinned: true } : t));
    });
  }

  unpin(thread: MessageThread): void {
    this.messagingService.unpinThread(thread.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.threads.update(list => list.map(t => t.id === thread.id ? { ...t, isPinned: false } : t));
    });
  }

  archive(thread: MessageThread): void {
    this.messagingService.archiveThread(thread.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.threads.update(list => list.map(t => t.id === thread.id ? { ...t, isArchived: true } : t));
    });
  }

  deleteThread(thread: MessageThread): void {
    this.messagingService.deleteThread(thread.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.threads.update(list => list.filter(t => t.id !== thread.id));
    });
  }
}
