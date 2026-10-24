import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { BehaviorSubject, catchError, forkJoin, map, of, startWith, switchMap } from 'rxjs';
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
import { HouseholdResponseModel } from '../core/models/household-response.model';
import { ErrorBanner } from '../shared/components/error-banner/error-banner.component';

type RecipientLoad =
  | { state: 'loading' }
  | { state: 'error' }
  | { state: 'ready'; me: number; households: HouseholdResponseModel[] };

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
    ErrorBanner,
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

  private reload$ = new BehaviorSubject<void>(undefined);
  private load = toSignal(this.reload$.pipe(
    switchMap(() => {
      this.householdStore.invalidate();
      return forkJoin({
        households: this.householdStore.getHouseholds(),
        me: this.authService.ensurePersonId(),
      }).pipe(
        map(({ households, me }): RecipientLoad => me ? { state: 'ready', me, households } : { state: 'error' }),
        catchError(() => of<RecipientLoad>({ state: 'error' })),
        startWith<RecipientLoad>({ state: 'loading' }),
      );
    }),
  ), { initialValue: { state: 'loading' } as RecipientLoad });
  loaded = computed(() => this.load().state === 'ready');
  loadFailed = computed(() => this.load().state === 'error');
  private otherMembers = computed(() => {
    const load = this.load();
    if (load.state !== 'ready') return [];
    const byPerson = new Map<number, HouseholdMemberResponseModel>();
    for (const household of load.households) {
      for (const member of household.members ?? []) {
        if (member.personId !== load.me && !byPerson.has(member.personId)) {
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

  retryLoad(): void {
    this.reload$.next();
  }

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
