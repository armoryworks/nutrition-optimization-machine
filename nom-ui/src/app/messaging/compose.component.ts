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
import { HouseholdStore } from '../core/services/household-store';
import { LoadingService } from '../core/services/loading.service';
import { AuthService } from '../core/services/auth.service';
import { HouseholdMemberResponseModel } from '../core/models/household-member-response.model';

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
  private householdStore = inject(HouseholdStore);
  private loadingService = inject(LoadingService);
  private authService = inject(AuthService);

  private households = toSignal(this.householdStore.getHouseholds());
  loaded = computed(() => this.households() !== undefined);
  private otherMembers = computed(() => {
    const me = this.authService.personId();
    const byPerson = new Map<number, HouseholdMemberResponseModel>();
    for (const household of this.households() ?? []) {
      for (const member of household.members ?? []) {
        if (member.personId !== me && !byPerson.has(member.personId)) {
          byPerson.set(member.personId, member);
        }
      }
    }
    return [...byPerson.values()];
  });
  recipients = computed(() => this.otherMembers().filter(m => m.hasAccount));
  unreachable = computed(() => this.otherMembers().filter(m => !m.hasAccount));
  unreachableNames = computed(() => this.unreachable().map(m => m.personName).join(', '));
  sending = signal(false);
  errorMessage = signal('');

  composeForm = this.fb.group({
    recipientIds: [[] as number[], Validators.required],
    message: ['', [Validators.required, Validators.pattern(/\S/)]],
  });

  onSubmit(): void {
    if (this.composeForm.invalid) {
      this.composeForm.markAllAsTouched();
      return;
    }
    if (this.sending()) return;
    this.sending.set(true);
    this.errorMessage.set('');

    const form = this.composeForm.getRawValue();
    this.messagingService.createThread({
      participantIds: form.recipientIds!,
      initialMessage: form.message!.trim(),
    }).pipe(
      this.loadingService.loading('Sending message...')
    ).subscribe({
      next: (result) => {
        this.sending.set(false);
        this.router.navigate(['/messages', result.threadId]);
      },
      error: (err) => {
        this.sending.set(false);
        const detail = err?.status === 400 ? err?.error?.detail : null;
        this.errorMessage.set(detail || 'Failed to send message. Please try again.');
      },
    });
  }
}
