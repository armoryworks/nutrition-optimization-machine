import { Injectable, effect, inject } from '@angular/core';
import { Observable, catchError, shareReplay, tap, throwError } from 'rxjs';
import { HouseholdService } from './household.service';
import { AuthService } from './auth.service';
import { HouseholdResponseModel } from '../models/household-response.model';
import { HouseholdCreateModel } from '../models/household-create.model';
import { HouseholdCreateResponseModel } from '../models/household-create-response.model';
import { HouseholdMemberResponseModel } from '../models/household-member-response.model';

/**
 * Caching layer over HouseholdService.getHouseholds(). The household list was
 * previously fetched independently by 10 components on every navigation; this
 * store fetches once and shares the result until it is invalidated by a
 * membership-changing mutation or an auth change.
 */
@Injectable({ providedIn: 'root' })
export class HouseholdStore {
  private householdService = inject(HouseholdService);
  private authService = inject(AuthService);
  private households$: Observable<HouseholdResponseModel[]> | null = null;

  constructor() {
    // A different user must never see a cached household list.
    effect(() => {
      this.authService.isLoggedIn();
      this.invalidate();
    });
  }

  getHouseholds(): Observable<HouseholdResponseModel[]> {
    if (!this.households$) {
      this.households$ = this.householdService.getHouseholds().pipe(
        catchError((err) => {
          // Do not cache failures; the next call retries.
          this.invalidate();
          return throwError(() => err);
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    }
    return this.households$;
  }

  invalidate(): void {
    this.households$ = null;
  }

  createHousehold(model: HouseholdCreateModel): Observable<HouseholdCreateResponseModel> {
    return this.householdService.createHousehold(model).pipe(tap(() => this.invalidate()));
  }

  joinHousehold(token: string): Observable<HouseholdMemberResponseModel> {
    return this.householdService.joinHousehold(token).pipe(tap(() => this.invalidate()));
  }

  convertToShared(householdId: number, name: string): Observable<HouseholdResponseModel> {
    return this.householdService.convertToShared(householdId, name).pipe(tap(() => this.invalidate()));
  }

  createPersonalHousehold(): Observable<HouseholdCreateResponseModel> {
    return this.householdService.createPersonalHousehold().pipe(tap(() => this.invalidate()));
  }
}
