import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil, finalize } from 'rxjs';
import {
  AmwCardComponent,
  AmwButtonComponent,
  AmwProgressSpinnerComponent,
  AmwIconComponent,
  loading,
} from 'angular-material-wrap';

import { HouseholdService } from '../../services/household.service';
import { HouseholdMemberResponseModel } from '../../models/household-member-response.model';
import { NotificationService } from '../../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../../shared/constants/error-messages';

@Component({
  selector: 'nom-household-join',
  standalone: true,
  imports: [
    CommonModule,
    AmwCardComponent,
    AmwButtonComponent,
    AmwProgressSpinnerComponent,
    AmwIconComponent,
  ],
  templateUrl: './household-join.component.html',
  styleUrl: './household-join.component.scss',
})
export class HouseholdJoinComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private householdService = inject(HouseholdService);
  private notificationService = inject(NotificationService);

  // Signals
  token = signal<string>('');
  isLoading = signal(true);
  isJoining = signal(false);
  error = signal<string | null>(null);
  memberInfo = signal<HouseholdMemberResponseModel | null>(null);
  joined = signal(false);

  // RxJS cleanup
  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.route.params.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const token = params['token'];
      if (token) {
        this.token.set(token);
        this.validateToken();
      } else {
        this.error.set('No invite token provided');
        this.isLoading.set(false);
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private validateToken(): void {
    this.isLoading.set(true);
    this.error.set(null);

    // For now, we'll just mark loading as complete
    // Token validation happens when user clicks Join
    this.isLoading.set(false);
  }

  onJoinHousehold(): void {
    this.isJoining.set(true);
    this.error.set(null);

    this.householdService
      .joinHousehold(this.token())
      .pipe(
        loading('Joining household...'),
        takeUntil(this.destroy$),
        finalize(() => this.isJoining.set(false))
      )
      .subscribe({
        next: (member: HouseholdMemberResponseModel) => {
          this.memberInfo.set(member);
          this.joined.set(true);
          this.notificationService.success('Successfully joined household!');

          // Navigate to household detail after 2 seconds
          setTimeout(() => {
            this.router.navigate(['/household', member.householdId]);
          }, 2000);
        },
        error: (err: any) => {
          console.error('Error joining household:', err);

          // Handle specific error messages from backend
          if (err.status === 401 || err.status === 400) {
            this.error.set(err.error?.message || 'Invalid or expired invite token');
          } else {
            this.error.set(ERROR_MESSAGES.HOUSEHOLD.JOIN_FAILED);
          }

          this.notificationService.error(this.error()!);
        },
      });
  }

  onBack(): void {
    this.router.navigate(['/household']);
  }
}
