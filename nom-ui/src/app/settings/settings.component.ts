import { Component, computed, inject, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../core/services/auth.service';
import { HouseholdStore } from '../core/services/household-store';

@Component({
  selector: 'nom-settings',
  imports: [RouterLink, MatIconModule],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Settings {
  private authService = inject(AuthService);
  private householdStore = inject(HouseholdStore);
  private destroyRef = inject(DestroyRef);

  isAdmin = this.authService.isAdmin;

  private households = toSignal(
    this.householdStore.getHouseholds().pipe(catchError(() => of([]))),
    { initialValue: [] },
  );

  /** A solo user's personal kitchen is labeled "My Kitchen", not "Household". */
  isPersonalKitchen = computed(() => !!this.households()[0]?.isPersonal);

  constructor() {
    this.authService.ensureAdminStatus().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }
}
