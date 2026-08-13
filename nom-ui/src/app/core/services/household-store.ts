import { Injectable, Signal, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { BehaviorSubject, Observable, catchError, of, shareReplay, switchMap, tap } from 'rxjs';
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

  // Emitting on this re-runs the fetch; all subscribers (imperative and the
  // reactive `households` signal below) receive the fresh list. Errors are
  // swallowed to [] so one transient failure can't permanently break the
  // shared stream — the empty list falls back to the no-household CTA, which
  // self-heals on the next mutation.
  private reload$ = new BehaviorSubject<void>(undefined);
  private households$: Observable<HouseholdResponseModel[]> = this.reload$.pipe(
    switchMap(() => this.householdService.getHouseholds().pipe(catchError(() => of([])))),
    shareReplay({ bufferSize: 1, refCount: false }),
  );

  /** Reactive household list — re-emits automatically after any invalidate(). */
  readonly households: Signal<HouseholdResponseModel[]> = toSignal(this.households$, {
    initialValue: [] as HouseholdResponseModel[],
  });

  constructor() {
    // A different user must never see a cached household list.
    effect(() => {
      this.authService.isLoggedIn();
      this.invalidate();
    });
  }

  getHouseholds(): Observable<HouseholdResponseModel[]> {
    return this.households$;
  }

  invalidate(): void {
    this.reload$.next();
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
