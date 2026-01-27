import { Component, OnInit, OnDestroy, inject, signal, computed, ViewChild, ElementRef, AfterViewChecked } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Subject, takeUntil, finalize } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwTextareaComponent,
  AmwInlineLoadingComponent,
  AmwIconButtonComponent,
  AmwIconComponent,
  AmwValidationTooltipDirective,
  AmwValidationService,
  ValidationContext,
} from 'angular-material-wrap';
import { MessagingService } from '../../services/messaging.service';
import { MessageThreadModel } from '../../models/i-message-thread.model';
import { MessageModel } from '../../models/message.model';
import { SendMessageRequestModel } from '../../models/send-message-request.model';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-message-thread-detail',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwCardComponent,
    AmwButtonComponent,
    AmwTextareaComponent,
    AmwInlineLoadingComponent,
    AmwIconButtonComponent,
    AmwIconComponent,
    AmwValidationTooltipDirective,
  ],
  templateUrl: './message-thread-detail.component.html',
  styleUrl: './message-thread-detail.component.scss',
})
export class MessageThreadDetailComponent implements OnInit, OnDestroy, AfterViewChecked {
  private messagingService = inject(MessagingService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private validationService = inject(AmwValidationService);

  @ViewChild('messageContainer') messageContainer?: ElementRef;
  validationContext!: ValidationContext;

  // Signals
  threadId = signal<number | null>(null);
  thread = signal<MessageThreadModel | null>(null);
  messages = signal<MessageModel[]>([]);
  isLoading = signal(true);
  isSending = signal(false);
  error = signal<string | null>(null);

  // Computed
  hasMessages = computed(() => this.messages().length > 0);
  participantNames = computed(() => {
    const currentThread = this.thread();
    if (currentThread?.participants) {
      return currentThread.participants.map(p => p.displayName).join(', ');
    }
    return 'Unknown';
  });

  // Form
  replyForm: FormGroup;

  // RxJS cleanup
  private destroy$ = new Subject<void>();
  private shouldScrollToBottom = false;

  constructor() {
    this.replyForm = this.fb.group({
      content: ['', [Validators.required, Validators.minLength(1)]],
    });
  }

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const id = params['id'];
      if (id) {
        this.threadId.set(+id);
        this.loadThreadDetails();
      } else {
        this.error.set('Invalid thread ID');
        this.isLoading.set(false);
      }
    });

    // Setup ValidationContext
    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Content validation - required
    this.validationService.addViolation(this.validationContext.id, {
      id: 'content-required',
      message: 'Message content is required',
      severity: 'error',
      field: 'content',
      control: this.replyForm.get('content') ?? undefined,
      validator: () => !this.replyForm.get('content')?.hasError('required')
    });

    // Content validation - minLength
    this.validationService.addViolation(this.validationContext.id, {
      id: 'content-minlength',
      message: 'Message must be at least 1 character',
      severity: 'error',
      field: 'content',
      control: this.replyForm.get('content') ?? undefined,
      validator: () => !this.replyForm.get('content')?.hasError('minlength')
    });
  }

  ngAfterViewChecked(): void {
    if (this.shouldScrollToBottom) {
      this.scrollToBottom();
      this.shouldScrollToBottom = false;
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();

    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  private loadThreadDetails(): void {
    const id = this.threadId();
    if (!id) return;

    this.isLoading.set(true);
    this.error.set(null);

    // Load thread info and messages in parallel
    this.messagingService
      .getMessageThread(id)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoading.set(false))
      )
      .subscribe({
        next: (thread) => {
          this.thread.set(thread);
          this.loadMessages();
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.COMMUNICATION.LOAD_FAILED);
          console.error('Error loading thread:', err);
        },
      });
  }

  private loadMessages(): void {
    const id = this.threadId();
    if (!id) return;

    this.messagingService
      .getMessages(id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (messages) => {
          this.messages.set(messages);
          this.shouldScrollToBottom = true;

          // Mark thread as read
          this.markThreadAsRead();
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.COMMUNICATION.LOAD_FAILED);
          console.error('Error loading messages:', err);
        },
      });
  }

  private markThreadAsRead(): void {
    const id = this.threadId();
    if (!id) return;

    this.messagingService.markThreadAsRead(id).pipe(takeUntil(this.destroy$)).subscribe({
      error: (err) => {
        console.error('Error marking thread as read:', err);
      },
    });
  }

  onSendMessage(): void {
    if (this.replyForm.invalid || this.isSending()) return;

    const id = this.threadId();
    if (!id) return;

    const content = this.replyForm.value.content?.trim();
    if (!content) return;

    this.isSending.set(true);

    const request: SendMessageRequestModel = {
      threadId: id,
      content: content,
    };

    this.messagingService
      .sendMessage(request)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isSending.set(false))
      )
      .subscribe({
        next: () => {
          // Clear form and reload messages
          this.replyForm.reset();
          this.loadMessages();
        },
        error: (err) => {
          this.error.set(ERROR_MESSAGES.COMMUNICATION.SEND_FAILED);
          console.error('Error sending message:', err);
        },
      });
  }

  onArchive(): void {
    const id = this.threadId();
    if (!id) return;

    this.messagingService.archiveThread(id).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.router.navigate(['/messaging']);
      },
      error: (err) => {
        this.error.set(ERROR_MESSAGES.COMMUNICATION.DELETE_FAILED);
        console.error('Error archiving thread:', err);
      },
    });
  }

  onPin(): void {
    const id = this.threadId();
    if (!id) return;

    const currentThread = this.thread();
    if (!currentThread) return;

    const operation = currentThread.isPinned
      ? this.messagingService.unpinThread(id)
      : this.messagingService.pinThread(id);

    operation.pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        // Update thread state
        this.thread.set({ ...currentThread, isPinned: !currentThread.isPinned });
      },
      error: (err) => {
        this.error.set(ERROR_MESSAGES.COMMUNICATION.SEND_FAILED);
        console.error('Error updating pin status:', err);
      },
    });
  }

  onDelete(): void {
    const id = this.threadId();
    if (!id) return;

    if (!confirm('Are you sure you want to delete this conversation?')) {
      return;
    }

    this.messagingService.deleteThread(id).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.router.navigate(['/messaging']);
      },
      error: (err) => {
        this.error.set(ERROR_MESSAGES.COMMUNICATION.DELETE_FAILED);
        console.error('Error deleting thread:', err);
      },
    });
  }

  onRetry(): void {
    this.loadThreadDetails();
  }

  onBack(): void {
    this.router.navigate(['/messaging']);
  }

  getMessageTime(message: MessageModel): string {
    const date = new Date(message.timestamp);
    const now = new Date();
    const diffInMinutes = (now.getTime() - date.getTime()) / (1000 * 60);

    if (diffInMinutes < 1) {
      return 'Just now';
    } else if (diffInMinutes < 60) {
      return `${Math.floor(diffInMinutes)}m ago`;
    } else if (diffInMinutes < 1440) {
      // Less than 24 hours
      return `${Math.floor(diffInMinutes / 60)}h ago`;
    } else {
      return date.toLocaleString();
    }
  }

  getParticipantById(personId: number): string {
    const currentThread = this.thread();
    if (currentThread?.participants) {
      const participant = currentThread.participants.find((p) => p.id === personId);
      return participant?.displayName || 'Unknown';
    }
    return 'Unknown';
  }

  getAvatarUrl(personId: number): string | undefined {
    const currentThread = this.thread();
    if (currentThread?.participants) {
      const participant = currentThread.participants.find((p) => p.id === personId);
      return participant?.avatarUrl;
    }
    return undefined;
  }

  private scrollToBottom(): void {
    try {
      if (this.messageContainer) {
        this.messageContainer.nativeElement.scrollTop = this.messageContainer.nativeElement.scrollHeight;
      }
    } catch (err) {
      console.error('Error scrolling to bottom:', err);
    }
  }
}
