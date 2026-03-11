import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MessagingService } from '../core/services/messaging.service';
import { HouseholdService } from '../core/services/household.service';
import { LoadingService } from '../core/services/loading.service';

@Component({
  selector: 'nom-compose',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './compose.component.html',
  styleUrl: './compose.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Compose {
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private messagingService = inject(MessagingService);
  private householdService = inject(HouseholdService);
  private loadingService = inject(LoadingService);

  private households = toSignal(this.householdService.getHouseholds(), { initialValue: [] });
  members = computed(() => {
    const list = this.households();
    return list.length > 0 && list[0].members ? list[0].members : [];
  });
  sending = signal(false);
  errorMessage = signal('');

  composeForm = this.fb.group({
    recipientIds: [[] as number[], Validators.required],
    subject: ['', [Validators.required, Validators.maxLength(255)]],
    message: ['', Validators.required],
  });

  onSubmit(): void {
    if (this.composeForm.invalid || this.sending()) return;
    this.sending.set(true);
    this.errorMessage.set('');

    const form = this.composeForm.getRawValue();
    this.messagingService.createThread({
      participantPersonIds: form.recipientIds!,
      subject: form.subject!,
      initialMessage: form.message!,
    }).pipe(
      this.loadingService.loading('Sending message...')
    ).subscribe({
      next: (result) => {
        this.sending.set(false);
        this.router.navigate(['/messages', result.id]);
      },
      error: () => {
        this.sending.set(false);
        this.errorMessage.set('Failed to send message. Please try again.');
      },
    });
  }
}
