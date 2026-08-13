import { Component, inject, input, signal, computed, effect, untracked, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { EnrollmentService } from '../../core/services/enrollment.service';
import { LoadingService } from '../../core/services/loading.service';
import { HouseholdEnrollmentInfo } from '../../core/models/household-enrollment-info.model';

/**
 * Per-adult enrollment consent screen (design doc §5): an adult member of a
 * managed household reviews what the provider can and cannot do, then records
 * their own acceptance directly with the Brigade API.
 */
@Component({
  selector: 'nom-enrollment-consent',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './enrollment-consent.component.html',
  styleUrl: './enrollment-consent.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EnrollmentConsent {
  /** Route param (component input binding). */
  id = input.required<string>();

  private enrollmentService = inject(EnrollmentService);
  private loadingService = inject(LoadingService);
  private destroyRef = inject(DestroyRef);

  info = signal<HouseholdEnrollmentInfo | null>(null);
  loading = signal(true);
  accepting = signal(false);
  accepted = signal(false);
  errorMessage = signal('');

  householdId = computed(() => Number(this.id()));
  featureEnabled = this.enrollmentService.brigadeEnabled;

  /** Provider label for the copy; Brigade owns the display name (may be null). */
  providerLabel = computed(() =>
    this.info()?.providerDisplayName || this.info()?.managedBy || 'your provider');

  isManaged = computed(() => !!this.info()?.managedBy);

  alreadyAccepted = computed(() =>
    this.accepted() || this.enrollmentService.hasAccepted(this.householdId()));

  /** What the provider can and cannot do — static v1 copy (legal-gated, see template note). */
  readonly grants = [
    'Lock dietary restrictions so they cannot be removed while you are enrolled',
    'Assign program policies (feature limits, curated recipes, frequency caps)',
    'See your household\'s meal plans and cooking adherence',
  ];
  readonly limits = [
    'They cannot see your private notes',
    'They cannot see recipes you create for yourself',
  ];

  private loadOnId = effect(() => {
    const householdId = Number(this.id());
    untracked(() => this.load(householdId));
  });

  accept(): void {
    const householdId = this.householdId();
    if (!householdId || this.accepting()) return;

    this.accepting.set(true);
    this.errorMessage.set('');

    this.enrollmentService.consentByHousehold(householdId).pipe(
      this.loadingService.loading('Recording your acceptance...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.accepting.set(false);
        this.accepted.set(true);
      },
      error: (err) => {
        this.accepting.set(false);
        this.errorMessage.set(this.friendlyError(err));
      },
    });
  }

  private load(householdId: number): void {
    if (!this.featureEnabled || !householdId) {
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.enrollmentService.getEnrollmentInfo(householdId).pipe(
      this.loadingService.loading('Loading enrollment...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (info) => {
        this.info.set(info);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Unable to load enrollment details. Please try again.');
      },
    });
  }

  private friendlyError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 404) {
        return 'No active enrollment was found for this household. It may have ended — ask your household steward.';
      }
      if (typeof err.error?.message === 'string' && err.error.message) {
        return err.error.message;
      }
    }
    return 'Unable to record your acceptance. Please try again.';
  }
}
