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
  AmwValidationTooltipDirective,
  AmwValidationService,
  ValidationContext,
} from 'angular-material-wrap';

import { MessagingService } from '../../services/messaging.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

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
    AmwValidationTooltipDirective,
  ],
  templateUrl: './message-compose.component.html',
  styleUrl: './message-compose.component.scss',
})
export class MessageComposeComponent implements OnInit, OnDestroy {
  private messagingService = inject(MessagingService);
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private validationService = inject(AmwValidationService);

  // Signals
  isCreating = signal(false);
  error = signal<string | null>(null);
  validationContext!: ValidationContext;

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
    // Setup ValidationContext
    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Participant IDs validation - required
    this.validationService.addViolation(this.validationContext.id, {
      id: 'participantIds-required',
      message: 'Participant IDs are required',
      severity: 'error',
      field: 'participantIds',
      control: this.composeForm.get('participantIds') ?? undefined,
      validator: () => !this.composeForm.get('participantIds')?.hasError('required')
    });

    // Participant IDs validation - pattern
    this.validationService.addViolation(this.validationContext.id, {
      id: 'participantIds-pattern',
      message: 'Please enter valid participant IDs (comma-separated numbers)',
      severity: 'error',
      field: 'participantIds',
      control: this.composeForm.get('participantIds') ?? undefined,
      validator: () => !this.composeForm.get('participantIds')?.hasError('pattern')
    });

    // Initial message validation - required
    this.validationService.addViolation(this.validationContext.id, {
      id: 'initialMessage-required',
      message: 'Message is required',
      severity: 'error',
      field: 'initialMessage',
      control: this.composeForm.get('initialMessage') ?? undefined,
      validator: () => !this.composeForm.get('initialMessage')?.hasError('required')
    });

    // Initial message validation - minLength
    this.validationService.addViolation(this.validationContext.id, {
      id: 'initialMessage-minlength',
      message: 'Message must be at least 1 character',
      severity: 'error',
      field: 'initialMessage',
      control: this.composeForm.get('initialMessage') ?? undefined,
      validator: () => !this.composeForm.get('initialMessage')?.hasError('minlength')
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();

    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
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
          this.error.set(ERROR_MESSAGES.COMMUNICATION.SEND_FAILED);
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
          this.error.set(ERROR_MESSAGES.COMMUNICATION.SEND_FAILED);
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
