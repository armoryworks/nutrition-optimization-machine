import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Subject, takeUntil, finalize } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwInputComponent,
  AmwTextareaComponent,
  AmwProgressSpinnerComponent,
} from 'angular-material-wrap';

import { MessagingService } from '../../services/messaging.service';

@Component({
  selector: 'nom-message-compose',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwCardComponent,
    AmwButtonComponent,
    AmwInputComponent,
    AmwTextareaComponent,
    AmwProgressSpinnerComponent,
  ],
  templateUrl: './message-compose.component.html',
  styleUrl: './message-compose.component.scss',
})
export class MessageComposeComponent implements OnInit, OnDestroy {
  private messagingService = inject(MessagingService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  // Signals
  isCreating = signal(false);
  error = signal<string | null>(null);

  // Form
  composeForm: FormGroup;

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  constructor() {
    this.composeForm = this.fb.group({
      participantIds: ['', [Validators.required, Validators.pattern(/^[\d,\s]+$/)]],
      initialMessage: ['', [Validators.required, Validators.minLength(1)]],
    });
  }

  ngOnInit(): void {
    // Component initialization
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onCreateThread(): void {
    if (this.composeForm.invalid || this.isCreating()) return;

    const participantIdsRaw = this.composeForm.value.participantIds?.trim();
    const initialMessage = this.composeForm.value.initialMessage?.trim();

    if (!participantIdsRaw || !initialMessage) return;

    // Parse participant IDs from comma-separated string
    const participantIds = participantIdsRaw
      .split(',')
      .map((id: string) => parseInt(id.trim(), 10))
      .filter((id: number) => !isNaN(id));

    if (participantIds.length === 0) {
      this.error.set('Please enter valid participant IDs');
      return;
    }

    this.isCreating.set(true);
    this.error.set(null);

    // Create thread first
    this.messagingService
      .createThread(participantIds)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isCreating.set(false))
      )
      .subscribe({
        next: (response) => {
          // Thread created, now send the initial message
          const threadId = response.threadId;
          this.sendInitialMessage(threadId, initialMessage);
        },
        error: (err) => {
          this.error.set('Failed to create conversation');
          console.error('Error creating thread:', err);
        },
      });
  }

  private sendInitialMessage(threadId: number, content: string): void {
    this.messagingService
      .sendMessage({ threadId, content })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          // Navigate to the new thread
          this.router.navigate(['/messaging/thread', threadId]);
        },
        error: (err) => {
          this.error.set('Failed to send initial message');
          console.error('Error sending initial message:', err);
          // Even if message fails, navigate to thread
          this.router.navigate(['/messaging/thread', threadId]);
        },
      });
  }

  onCancel(): void {
    this.router.navigate(['/messaging']);
  }

  onRetry(): void {
    this.error.set(null);
  }
}
