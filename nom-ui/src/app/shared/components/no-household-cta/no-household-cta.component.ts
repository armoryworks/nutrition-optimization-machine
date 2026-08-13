import { Component, inject, input, output, signal, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { HttpErrorResponse } from '@angular/common/http';
import { HouseholdStore } from '../../../core/services/household-store';
import { AuthService } from '../../../core/services/auth.service';

/**
 * The ONE shared call-to-action for anywhere a user is blocked by not having
 * a household set up (empty states and household-absence error states alike):
 * a solo primary that silently creates a personal kitchen server-side, and a
 * secondary into the normal household-creation flow, with the benefits note.
 *
 * The host page provides its own contextual wording (or passes `message`)
 * and reloads its state in place on `(created)`.
 */
@Component({
  selector: 'nom-no-household-cta',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './no-household-cta.component.html',
  styleUrl: './no-household-cta.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NoHouseholdCta {
  /** Optional short contextual line above the buttons. */
  message = input('');
  /** Hide the secondary button where the create form is already on screen. */
  showCreate = input(true);

  /** Fires once the personal kitchen exists and claims are refreshed — reload page state in place. */
  created = output<void>();

  private householdStore = inject(HouseholdStore);
  private authService = inject(AuthService);
  private destroyRef = inject(DestroyRef);

  creating = signal(false);
  errorMessage = signal('');

  createSolo(): void {
    if (this.creating()) return;
    this.creating.set(true);
    this.errorMessage.set('');

    this.householdStore.createPersonalHousehold().pipe(
      switchMap(() => this.authService.refreshClaims()),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.creating.set(false);
        this.created.emit();
      },
      error: (err) => {
        this.creating.set(false);
        if (err instanceof HttpErrorResponse && err.error?.reason === 'already_in_household') {
          // Stale local state — a household exists; let the host reload.
          this.householdStore.invalidate();
          this.created.emit();
          return;
        }
        this.errorMessage.set('Unable to set things up. Please try again.');
      },
    });
  }
}
